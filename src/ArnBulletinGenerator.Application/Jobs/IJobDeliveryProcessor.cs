using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Application.Jobs;

/// <summary>
/// Owns the asynchronous delivery worker boundary without widening the legacy job-service
/// contract implemented by test and integration adapters.
/// </summary>
public interface IJobDeliveryProcessor
{
    Task<Result> ProcessDeliveryAsync(
        DeliveryQueueCommand command,
        CancellationToken cancellationToken = default);
}
