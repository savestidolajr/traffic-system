using System.ComponentModel.DataAnnotations;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Configuration;

public sealed class ConfigurationOptionsTests
{
    // ── WebhookOptions.Validate ──

    [Fact]
    public void WebhookOptions_Validate_DisabledWithNoUrl_NoErrors()
    {
        var options = new WebhookOptions { Enabled = false, EndpointUrl = "" };

        var results = options.Validate(new ValidationContext(options)).ToList();

        results.ShouldBeEmpty();
    }

    [Fact]
    public void WebhookOptions_Validate_EnabledWithValidUrl_NoErrors()
    {
        var options = new WebhookOptions
        {
            Enabled = true,
            EndpointUrl = "https://prod-01.australiaeast.logic.azure.com/workflows/abc"
        };

        var results = options.Validate(new ValidationContext(options)).ToList();

        results.ShouldBeEmpty();
    }

    [Fact]
    public void WebhookOptions_Validate_EnabledWithEmptyUrl_ReturnsError()
    {
        var options = new WebhookOptions { Enabled = true, EndpointUrl = "" };

        var results = options.Validate(new ValidationContext(options)).ToList();

        var item = results.ShouldHaveSingleItem();
        item.MemberNames.ShouldContain(nameof(WebhookOptions.EndpointUrl));
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("/relative/path")]
    [InlineData("://no-scheme")]
    public void WebhookOptions_Validate_EnabledWithInvalidUrl_ReturnsError(string url)
    {
        var options = new WebhookOptions { Enabled = true, EndpointUrl = url };

        var results = options.Validate(new ValidationContext(options)).ToList();

        results.ShouldHaveSingleItem();
    }

    [Fact]
    public void WebhookOptions_Validate_DisabledWithInvalidUrl_NoErrors()
    {
        var options = new WebhookOptions { Enabled = false, EndpointUrl = "garbage" };

        var results = options.Validate(new ValidationContext(options)).ToList();

        results.ShouldBeEmpty();
    }

    // ── ElevenLabsOptions.ApiBaseUrl ──

    [Fact]
    public void ElevenLabsOptions_ApiBaseUrl_StandardUrl_StripsTextToSpeechSuffix()
    {
        var options = new ElevenLabsOptions
        {
            BaseUrl = "https://api.elevenlabs.io/v1/text-to-speech"
        };

        options.ApiBaseUrl.ShouldBe("https://api.elevenlabs.io/v1");
    }

    [Fact]
    public void ElevenLabsOptions_ApiBaseUrl_CaseInsensitiveMatch()
    {
        var options = new ElevenLabsOptions
        {
            BaseUrl = "https://api.elevenlabs.io/v1/Text-To-Speech"
        };

        options.ApiBaseUrl.ShouldBe("https://api.elevenlabs.io/v1");
    }

    [Fact]
    public void ElevenLabsOptions_ApiBaseUrl_NoTextToSpeechSuffix_ReturnsBaseUrlUnchanged()
    {
        var options = new ElevenLabsOptions
        {
            BaseUrl = "https://custom-proxy.example.com/api"
        };

        options.ApiBaseUrl.ShouldBe("https://custom-proxy.example.com/api");
    }

    [Fact]
    public void ElevenLabsOptions_ApiBaseUrl_TrailingSlashAfterSuffix_StripsCorrectly()
    {
        var options = new ElevenLabsOptions
        {
            BaseUrl = "https://api.elevenlabs.io/v1/text-to-speech/voiceid"
        };

        // Should strip from the first occurrence of /text-to-speech
        options.ApiBaseUrl.ShouldBe("https://api.elevenlabs.io/v1");
    }

    // ── StorageOptions.IsLocalDevelopment ──

    [Fact]
    public void StorageOptions_IsLocalDevelopment_AzuriteAccount_ReturnsTrue()
    {
        var options = new StorageOptions { AccountName = "devstoreaccount1" };

        options.IsLocalDevelopment.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void StorageOptions_IsLocalDevelopment_NullOrEmpty_ReturnsTrue(string? accountName)
    {
        var options = new StorageOptions { AccountName = accountName! };

        options.IsLocalDevelopment.ShouldBeTrue();
    }

    [Fact]
    public void StorageOptions_IsLocalDevelopment_RealAccount_ReturnsFalse()
    {
        var options = new StorageOptions { AccountName = "starntraffic" };

        options.IsLocalDevelopment.ShouldBeFalse();
    }

    [Fact]
    public void StorageOptions_LocalEmulatorAccount_MatchesAzuriteDefault()
    {
        StorageOptions.LocalEmulatorAccount.ShouldBe("devstoreaccount1");
    }

    // ── ProviderOptions.IsAzureBlobOutput ──

    [Fact]
    public void ProviderOptions_IsAzureBlobOutput_AzureBlob_ReturnsTrue()
    {
        var options = new ProviderOptions { OutputProvider = OutputProviderType.AzureBlob };

        options.IsAzureBlobOutput.ShouldBeTrue();
    }

    [Fact]
    public void ProviderOptions_IsAzureBlobOutput_Console_ReturnsFalse()
    {
        var options = new ProviderOptions { OutputProvider = OutputProviderType.Console };

        options.IsAzureBlobOutput.ShouldBeFalse();
    }

    [Fact]
    public void ProviderOptions_Default_IsConsole()
    {
        var options = new ProviderOptions();

        options.OutputProvider.ShouldBe(OutputProviderType.Console);
        options.IsAzureBlobOutput.ShouldBeFalse();
    }
}
