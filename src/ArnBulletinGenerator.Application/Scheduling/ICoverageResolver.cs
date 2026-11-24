using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Application.Scheduling;

/// <summary>
///     Contract for filling in an orchestration request's coverage area by geocoding its
///     location when coverage was not supplied.
/// </summary>
public interface ICoverageResolver
{
    Task<Result<OrchestrationRequestDto>> ResolveCoverageAsync(OrchestrationRequestDto dto, CancellationToken ct);
}
