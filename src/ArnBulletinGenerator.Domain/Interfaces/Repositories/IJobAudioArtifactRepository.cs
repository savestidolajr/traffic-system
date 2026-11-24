using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;
/// <summary>Deletes only job-owned audio artifacts during a retention sweep.</summary>
public interface IJobAudioArtifactRepository
{
    Task<Result> DeleteAsync(string audioReference, CancellationToken cancellationToken = default);
}
