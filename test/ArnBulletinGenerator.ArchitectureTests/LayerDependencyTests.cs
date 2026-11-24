using System.Reflection;
using ArnBulletinGenerator.Application.Commands;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Functions;
using ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;
using NetArchTest.Rules;
using Shouldly;
using ArnBulletinGenerator.Functions.Triggers.Http;

namespace ArnBulletinGenerator.ArchitectureTests;

/// <summary>
/// Architecture-layer dependency rules for the Arn bulletin generator.
///
/// Clean Architecture layering (inner -> outer):
///   Domain -> Application -> Infrastructure -> Functions
///
/// Dependencies flow inward. Outer layers may reference inner layers;
/// inner layers must NOT reference outer layers.
/// </summary>
public sealed class LayerDependencyTests
{
    private static readonly Assembly DomainAssembly = typeof(ExponentialMovingAverage).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(CreateJobCommand).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(LlmScriptGenerator).Assembly;
    private static readonly Assembly FunctionsAssembly = typeof(BroadcastHttpTrigger).Assembly;

    private const string DomainNamespace = "ArnBulletinGenerator.Domain";
    private const string ApplicationNamespace = "ArnBulletinGenerator.Application";
    private const string InfrastructureNamespace = "ArnBulletinGenerator.Infrastructure";
    private const string FunctionsNamespace = "ArnBulletinGenerator.Functions";

    // ======================================================================
    // Domain rules
    // ======================================================================

    [Fact]
    public void Domain_ShouldNotDependOn_Application()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That().ResideInNamespace(DomainNamespace)
            .ShouldNot().HaveDependencyOn(ApplicationNamespace)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(result.ToString());
    }

    [Fact]
    public void Domain_ShouldNotDependOn_Infrastructure()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That().ResideInNamespace(DomainNamespace)
            .ShouldNot().HaveDependencyOn(InfrastructureNamespace)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(result.ToString());
    }

    [Fact]
    public void Domain_ShouldNotDependOn_Functions()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That().ResideInNamespace(DomainNamespace)
            .ShouldNot().HaveDependencyOn(FunctionsNamespace)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(result.ToString());
    }

    // ======================================================================
    // Application rules
    // ======================================================================

    [Fact]
    public void Application_ShouldNotDependOn_Infrastructure()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That().ResideInNamespace(ApplicationNamespace)
            .ShouldNot().HaveDependencyOn(InfrastructureNamespace)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(result.ToString());
    }

    [Fact]
    public void Application_ShouldNotDependOn_Functions()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That().ResideInNamespace(ApplicationNamespace)
            .ShouldNot().HaveDependencyOn(FunctionsNamespace)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(result.ToString());
    }

    // ======================================================================
    // Infrastructure rules
    // ======================================================================

    [Fact]
    public void Infrastructure_ShouldNotDependOn_Application()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .That().ResideInNamespace(InfrastructureNamespace)
            .ShouldNot().HaveDependencyOn(ApplicationNamespace)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(result.ToString());
    }

    [Fact]
    public void Infrastructure_ShouldNotDependOn_Functions()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .That().ResideInNamespace(InfrastructureNamespace)
            .ShouldNot().HaveDependencyOn(FunctionsNamespace)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(result.ToString());
    }

    // ======================================================================
    // Domain package dependency rules
    // ======================================================================

    [Fact]
    public void Domain_ShouldNotReference_AzurePackages()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That().ResideInNamespace(DomainNamespace)
            .ShouldNot().HaveDependencyOn("Azure")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(result.ToString());
    }

    [Fact]
    public void Domain_ShouldNotReference_SystemNetHttp()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That().ResideInNamespace(DomainNamespace)
            .ShouldNot().HaveDependencyOn("System.Net.Http")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(result.ToString());
    }
}
