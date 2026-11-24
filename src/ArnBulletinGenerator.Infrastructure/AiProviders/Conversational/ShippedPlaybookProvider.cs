using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Workflow.Prompts;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.Conversational;

/// <summary>Resolves the same on-disk defaults exposed by the playbook HTTP surface.</summary>
public sealed class ShippedPlaybookProvider : IShippedPlaybookProvider
{
    public Result<Playbook> GetDefault(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return Result<Playbook>.Failure("A playbook role is required.");

        string? canonical = BulletinPlaybook.CanonicalRole(role);
        if (canonical is null)
            return Result<Playbook>.Failure($"Unknown playbook role '{role}'.");

        string? path = FindDefaultPrompt(canonical);
        if (path is null)
            return Result<Playbook>.Failure($"The shipped default playbook for role '{canonical}' is unavailable.");

        try
        {
            return Result<Playbook>.Success(PlaybookParser.Parse(File.ReadAllText(path)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
        {
            return Result<Playbook>.Failure($"Could not load shipped default playbook for role '{canonical}': {exception.Message}");
        }
    }

    private static string? FindDefaultPrompt(string role)
    {
        string relativePath = role == "writer"
            ? Path.Combine("prompts", "agents", "arn-draft.md")
            : Path.Combine("src", "ArnBulletinGenerator.Workflow", "Prompts", $"{role}.md");

        string? overrideDir = Environment.GetEnvironmentVariable("ARN_PROMPTS_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            string overridePath = role == "writer"
                ? Path.Combine(overrideDir, "agents", "arn-draft.md")
                : Path.Combine(overrideDir, $"{role}.md");
            if (File.Exists(overridePath))
                return overridePath;
        }

        // Workflow roles ship beside the Workflow assembly (Prompts/<role>.md is copied to
        // output and published), which is the only resolution that exists on a deployed
        // Function App. Verified live 2026-08-27: the repo-walk below found nothing deployed
        // and the studio showed "shipped compose2 playbook is unavailable" while generation,
        // using PromptLibrary, worked fine.
        if (role != "writer")
        {
            string deployed = Path.Combine(PromptLibrary.Root, "Prompts", $"{role}.md");
            if (File.Exists(deployed))
                return deployed;
        }

        var roots = new List<string> { Directory.GetCurrentDirectory() };
        string? directory = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && directory is not null; i++)
        {
            roots.Add(directory);
            directory = Directory.GetParent(directory)?.FullName;
        }

        return roots.Select(root => Path.Combine(root, relativePath)).FirstOrDefault(File.Exists);
    }
}
