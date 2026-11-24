using System.Globalization;
using ArnBulletinGenerator.Workflow.Contracts;
using ArnBulletinGenerator.Workflow.Hosting;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Functions.Metrics;

/// <summary>
///     Emits one classic Application Insights dependency for each Azure OpenAI HTTP attempt.
///     Foundry's Tracing tab and Microsoft's own sample query read
///     <c>dependencies</c> on <c>customDimensions['gen_ai.operation.name']</c>, correlated by
///     <c>operation_Id</c>. These are therefore custom dimensions on a <see cref="DependencyTelemetry" />
///     rather than an <c>ActivitySource</c> span: this app uses the classic Application Insights
///     SDK 2.x, which has no OpenTelemetry tracer provider, so an ActivitySource span would export
///     nowhere. The per-attempt rows are also what expose a client-side serving stall that a
///     server-side Foundry agent trace cannot show.
/// </summary>
public sealed class ApplicationInsightsLlmCallTelemetry(
    TelemetryClient telemetryClient,
    AzureOpenAiWorkflowLlmOptions options,
    ILogger<ApplicationInsightsLlmCallTelemetry> logger) : ILlmCallTelemetry
{
    private const int MaxDimensionLength = 128;
    private const string OperationName = "chat";
    private const string ProviderName = "azure.ai.openai";

    private readonly string rawTarget = EndpointHost(options.Endpoint);
    private readonly string agentTarget = EndpointHost(options.ProjectEndpoint);

    public void Record(in LlmCallTelemetryRecord record)
    {
        try
        {
            string model = Canonical(record.Model);
            string role = Canonical(record.Role);
            string route = Canonical(record.Route);
            var dependency = new DependencyTelemetry
            {
                Type = "Azure AI",
                Target = string.Equals(route, "agent", StringComparison.Ordinal)
                    ? agentTarget
                    : rawTarget,
                Name = $"{OperationName} {model}",
                Duration = record.Duration,
                Success = record.Success,
                ResultCode = record.ResultCode
            };

            dependency.Properties["gen_ai.operation.name"] = OperationName;
            dependency.Properties["gen_ai.provider.name"] = ProviderName;
            dependency.Properties["gen_ai.request.model"] = model;
            dependency.Properties["gen_ai.response.model"] = model;
            dependency.Properties["arn.role"] = role;
            dependency.Properties["arn.route"] = route;
            dependency.Properties["arn.attempt"] = record.Attempt.ToString(CultureInfo.InvariantCulture);
            dependency.Properties["arn.stall_attempt"] =
                record.StallAttempt.ToString(CultureInfo.InvariantCulture);
            dependency.Properties["arn.stalled"] = record.Stalled ? "true" : "false";

            // Do not replace the operation context: TelemetryClient inherits the current request/
            // invocation operation_Id, which is the join key for request, dependency and trace
            // rows. This keeps unmatched dependency rows visible when no parent operation exists.
            // Whether this attempt ran on the alternate deployment is a bounded diagnostic flag.
            dependency.Properties["arn.via_fallback"] = record.ViaFallback ? "true" : "false";

            if (record.ResponseId is not null)
                dependency.Properties["gen_ai.response.id"] = record.ResponseId;
            if (record.InputTokens is { } inputTokens)
                dependency.Properties["gen_ai.usage.input_tokens"] =
                    inputTokens.ToString(CultureInfo.InvariantCulture);
            if (record.OutputTokens is { } outputTokens)
                dependency.Properties["gen_ai.usage.output_tokens"] =
                    outputTokens.ToString(CultureInfo.InvariantCulture);
            if (record.CachedInputTokens is { } cachedInputTokens)
                dependency.Properties["gen_ai.usage.cached_input_tokens"] =
                    cachedInputTokens.ToString(CultureInfo.InvariantCulture);
            if (record.ReasoningTokens is { } reasoningTokens)
                dependency.Properties["gen_ai.usage.reasoning_tokens"] =
                    reasoningTokens.ToString(CultureInfo.InvariantCulture);
            if (record.ErrorMessage is not null)
                dependency.Properties["error.message"] = record.ErrorMessage;

            telemetryClient.TrackDependency(dependency);
        }
        catch (Exception ex)
        {
            // A telemetry exporter failure must never turn an LLM attempt into a workflow failure.
            logger.LogDebug(ex, "LLM dependency telemetry dropped for {Role}", Canonical(record.Role));
        }
    }

    private static string Canonical(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        string normalized = value.Trim().ToLowerInvariant();
        return normalized.Length <= MaxDimensionLength
            ? normalized
            : normalized[..MaxDimensionLength];
    }

    private static string EndpointHost(string? endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ? uri.Host : string.Empty;
}
