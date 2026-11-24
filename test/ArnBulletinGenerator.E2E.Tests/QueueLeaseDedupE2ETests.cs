using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.Persistence;
using Azure.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace ArnBulletinGenerator.E2E.Tests;

/// <summary>
///     Exercises the real Table Storage lease row used by queue consumers. This is deliberately an
///     Azurite test: the duplicate and ETag paths must be proven against the Table SDK, not only a
///     substitute for <c>IJobClaimRepository</c>.
/// </summary>
[Trait("Category", "E2E")]
public sealed class QueueLeaseDedupE2ETests
{
    private readonly TableStorageJobClaimRepository _repository = new(
        Options.Create(new StorageOptions
        {
            AccountName = StorageOptions.LocalEmulatorAccount,
            ContainerName = "broadcasts",
            MaxRetries = 0
        }),
        new AzuriteTokenCredential(),
        NullLogger<TableStorageJobClaimRepository>.Instance);

    [Fact(Timeout = 120_000)]
    public async Task FreshClaimSuppressesDuplicate_RenewUpdatesEtag_ReleaseAllowsRecovery()
    {
        string jobId = $"queue-lease-{Guid.NewGuid():N}";
        var first = await _repository.TryClaimAsync(
            jobId, "worker-one", TimeSpan.FromMinutes(8));
        first.IsSuccess.ShouldBeTrue(first.Error);
        first.Value.ShouldNotBeNull();
        var firstClaim = first.Value!;
        firstClaim.ETag.ShouldNotBeNullOrWhiteSpace();

        var duplicate = await _repository.TryClaimAsync(
            jobId, "worker-two", TimeSpan.FromMinutes(8));
        duplicate.IsSuccess.ShouldBeTrue(duplicate.Error);
        duplicate.Value.ShouldBeNull();

        string priorEtag = firstClaim.ETag;
        DateTime priorExpiry = firstClaim.ExpiresUtc;
        var renewed = await _repository.RenewAsync(firstClaim);
        renewed.IsSuccess.ShouldBeTrue(renewed.Error);
        firstClaim.ExpiresUtc.ShouldBeGreaterThan(priorExpiry);
        firstClaim.ETag.ShouldNotBe(priorEtag);

        var released = await _repository.ReleaseAsync(firstClaim);
        released.IsSuccess.ShouldBeTrue(released.Error);

        var recovered = await _repository.TryClaimAsync(
            jobId, "worker-two", TimeSpan.FromMinutes(8));
        recovered.IsSuccess.ShouldBeTrue(recovered.Error);
        recovered.Value.ShouldNotBeNull();
        var recoveredClaim = recovered.Value!;
        recoveredClaim.Owner.ShouldBe("worker-two");
        (await _repository.ReleaseAsync(recoveredClaim)).IsSuccess.ShouldBeTrue();
    }

    private sealed class AzuriteTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            new("azurite-test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
}
