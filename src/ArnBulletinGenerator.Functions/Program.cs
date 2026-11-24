// Azure Functions Worker host entry point: wires config including Key Vault, registers DI services and middleware, then starts the worker host.
using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Functions;
using ArnBulletinGenerator.Infrastructure;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.ConfigTransfer;
using ArnBulletinGenerator.Functions.Metrics;
using ArnBulletinGenerator.Functions.Middleware;
using ArnBulletinGenerator.Functions.Triggers.Queues;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(workerApp =>
    {
        workerApp.UseMiddleware<GlobalExceptionHandlingMiddleware>();
        workerApp.UseMiddleware<AuthorizationMiddleware>();
        workerApp.UseMiddleware<ActivityLoggingMiddleware>();
    })
    .ConfigureAppConfiguration(config =>
    {
        config.AddJsonFile("appsettings.json", true, false);
        config.AddJsonFile("appsettings.Development.json", true, false);
        config.AddEnvironmentVariables();

        var built = config.Build();
        string? keyVaultUri = built["KeyVaultUri"];
        if (string.IsNullOrWhiteSpace(keyVaultUri))
            return;

        using var bootstrapLoggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
        var startupLogger = bootstrapLoggerFactory.CreateLogger("Startup");

        try
        {
            config.AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential());
        }
        catch (Exception ex) when (ex is CredentialUnavailableException or AuthenticationFailedException)
        {
            startupLogger.LogWarning(ex, "Key Vault credential issue during startup: {Message}", ex.Message);
        }
        catch (Exception ex)
        {
            startupLogger.LogError(ex, "Key Vault configuration failed: {Message}", ex.Message);
        }
    })
    .ConfigureServices((context, services) =>
    {
        services.AddApplication();
        services.AddMemoryCache();
        services.AddOptions<IncidentStateOptions>()
            .Bind(context.Configuration.GetSection(IncidentStateOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<MarketAccessOptions>()
            .Bind(context.Configuration.GetSection(MarketAccessOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddInfrastructure(context.Configuration);
        // Functions-layer composition service for the admin config-transfer surface
        // (ConfigImportService is Application-layer; registered by AddApplication()).
        services.AddScoped<ConfigExportBuilder>();
        services.AddScoped<PoisonQueueHandler>();
        // Shared by the SWA rolesSource bridge (sign-in) and /operators/me (Easy Auth direct).
        services.AddScoped<OperatorRegistrar>();
        // Stateless AAD sign-in validator for the SWA rolesSource bridge; safe as a singleton.
        services.AddSingleton<SwaRolesSourceTokenValidator>();
        var providerOptions = context.Configuration.GetSection(ProviderOptions.SectionName).Get<ProviderOptions>() ??
                              new ProviderOptions();
        if (providerOptions.UseFakeExternalProviders)
        {
            using var startupLoggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
            startupLoggerFactory.CreateLogger("Startup")
                .LogWarning("Fake external providers are enabled for deterministic E2E/local testing");
        }
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
        // Replace the no-op metrics sink: use the E2E adapter in fake mode so pipeline
        // stages emit greppable [E2E-Metric] markers; use AppInsights in real mode.
        if (providerOptions.UseFakeExternalProviders)
        {
            services.AddSingleton<IBroadcastMetrics, E2EFakeBroadcastMetrics>();
        }
        else
        {
            services.AddSingleton<IBroadcastMetrics, ApplicationInsightsBroadcastMetrics>();
            services.AddSingleton<IInteractiveWorkMetrics, ApplicationInsightsInteractiveWorkMetrics>();
        }
        // Fail-fast: Zetta delivery to playout must never coexist with the dev
        // auth bypass. An environment configured for live delivery while every
        // API caller is granted blanket Admin access is unsafe — a discarded test
        // session or mistaken deploy could air unauthenticated content. The check
        // reads raw config values rather than resolved IOptions to catch the unsafe
        // combination before any validation pipeline masks the issue.
        var zettaEnabled = context.Configuration.GetSection(ZettaOptions.SectionName).GetValue<bool>(nameof(ZettaOptions.Enabled));
        // Read BOTH spellings, exactly as AuthorizationMiddleware.ResolveDevFallbackRole does.
        // The deployment sets the literal environment-variable name Auth__DevFallbackRole, but the
        // environment-variable configuration provider rewrites `__` to `:` when it builds keys, so
        // the literal spelling resolves to null in a real Azure deployment. Reading only the literal
        // key made both guards below fail OPEN on precisely the hosted test/production sites they
        // exist to protect, while the middleware — which checks both — still applied the bypass.
        // Verified 2026-09-03: test carried Auth__DevFallbackRole=Admin, the host started normally,
        // and the middleware logged "DEV AUTH BYPASS active" 42 times.
        var devFallbackRole = context.Configuration[ClientPrincipal.DevFallbackRoleSetting]
            ?? context.Configuration["Auth:DevFallbackRole"];
        if (StartupSafety.IsUnsafeDeliveryConfig(zettaEnabled, devFallbackRole))
        {
            throw new InvalidOperationException(
                $"Startup aborted: {ZettaOptions.SectionName}.{nameof(ZettaOptions.Enabled)} is true " +
                $"but {ClientPrincipal.DevFallbackRoleSetting} is set to '{devFallbackRole}'. " +
                "Delivery to playout with a blanket dev auth bypass is unsafe. " +
                $"Clear {ClientPrincipal.DevFallbackRoleSetting} before enabling Zetta delivery.");
        }

        // Fail-fast: the dev RBAC bypass must never run in test or production. It
        // synthesizes an Admin principal for any caller with no principal header —
        // precisely an anonymous internet request arriving via the Static Web App.
        // WEBSITE_SITE_NAME is set by Azure and cannot be removed by configuration,
        // so it is a stronger signal than any app setting; absent (local/E2E) stays
        // permissive, and only a positively identified TST/PRD site is refused.
        var siteName = context.Configuration["WEBSITE_SITE_NAME"];
        if (StartupSafety.IsUnsafeAuthBypass(siteName, devFallbackRole))
        {
            throw new InvalidOperationException(
                $"Startup aborted: {ClientPrincipal.DevFallbackRoleSetting} is set to " +
                $"'{devFallbackRole}' on '{siteName}', which is not a development site. " +
                "This setting grants Admin to every caller that arrives without an " +
                "authenticated principal. Clear it before deploying.");
        }
    })
    .ConfigureLogging(logging =>
    {
        logging.Services.Configure<LoggerFilterOptions>(options =>
        {
            var defaultRule = options.Rules.FirstOrDefault(rule =>
                rule.ProviderName ==
                "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");
            if (defaultRule is not null)
                options.Rules.Remove(defaultRule);
        });
    })
    .Build();

await host.RunAsync();
