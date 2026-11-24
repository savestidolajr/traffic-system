using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Loads the shipped, block-marked default for a playbook role.</summary>
public interface IShippedPlaybookProvider
{
    Result<Playbook> GetDefault(string role);
}
