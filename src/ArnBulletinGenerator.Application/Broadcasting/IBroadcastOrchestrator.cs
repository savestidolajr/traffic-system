using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Contracts.Messages;
namespace ArnBulletinGenerator.Application.Broadcasting;

/// <summary>
///     Contract for running a bulletin through the full broadcast pipeline, either from a
///     fresh request or from traffic evidence already gathered for a regeneration.
/// </summary>
public interface IBroadcastOrchestrator
{
    Task<Result<BroadcastResult>> OrchestrateAsync(BroadcastRequest request, Phase phase,
        CancellationToken cancellationToken = default);

    Task<Result<BroadcastResult>> OrchestrateAsync(BroadcastWithTraffic context,
        CancellationToken cancellationToken = default);
}
