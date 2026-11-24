using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Infrastructure.Audio.DurationStrategies;

/// <summary>
///     Inserts tiny silence segments inside already-synthesized WAV audio to close small underruns
///     without changing script text or re-synthesizing through TTS.
/// </summary>
public sealed partial class SentencePauseInsertionDurationAdjustmentStrategy(
    ILogger<SentencePauseInsertionDurationAdjustmentStrategy> logger)
    : IAudioPostSynthesisDurationAdjustmentStrategy
{
    private const double MinResidualSeconds = 0.08;
    private const double MaxResidualSeconds = 1.20;
    private const double MinPausePerBoundarySeconds = 0.06;
    private const double MaxPausePerBoundarySeconds = 0.35;
    private const int MaxBoundariesToUse = 5;

    public string Name => "SentencePauseInsertion";

    public Task<AudioPostSynthesisAdjustmentProposal?> TryAdjustAsync(
        AudioPostSynthesisAdjustmentContext context,
        CancellationToken cancellationToken = default)
    {
        double residual = context.TargetDurationSeconds - context.ActualDurationSeconds;
        if (residual < MinResidualSeconds || residual > MaxResidualSeconds)
            return Task.FromResult<AudioPostSynthesisAdjustmentProposal?>(null);

        var wavInfo = WavInfoParser.GetWavInfo(context.Audio);
        if (wavInfo is null || wavInfo.BitsPerSample != 16 || wavInfo.DataSize <= 0)
            return Task.FromResult<AudioPostSynthesisAdjustmentProposal?>(null);

        var sentenceWordCounts = GetSentenceWordCounts(context.Script);
        if (sentenceWordCounts.Count < 2)
            return Task.FromResult<AudioPostSynthesisAdjustmentProposal?>(null);

        int boundaryCount = Math.Min(sentenceWordCounts.Count - 1, MaxBoundariesToUse);
        double pausePerBoundary = Math.Clamp(
            residual / boundaryCount,
            MinPausePerBoundarySeconds,
            MaxPausePerBoundarySeconds);
        if (pausePerBoundary <= 0)
            return Task.FromResult<AudioPostSynthesisAdjustmentProposal?>(null);

        int blockAlign = wavInfo.Channels * wavInfo.BitsPerSample / 8;
        if (blockAlign <= 0)
            return Task.FromResult<AudioPostSynthesisAdjustmentProposal?>(null);

        int pauseBytes =
            AudioMathHelper.AlignToBlockBoundary((int)Math.Round(pausePerBoundary * wavInfo.ByteRate), blockAlign);
        if (pauseBytes <= 0)
            return Task.FromResult<AudioPostSynthesisAdjustmentProposal?>(null);

        var insertionPoints = BuildInsertionPoints(sentenceWordCounts, wavInfo.DataSize, boundaryCount, blockAlign);
        if (insertionPoints.Count == 0)
            return Task.FromResult<AudioPostSynthesisAdjustmentProposal?>(null);

        byte[] adjusted = InsertSilenceAtOffsets(context.Audio, wavInfo, insertionPoints, pauseBytes);
        if (ReferenceEquals(adjusted, context.Audio))
            return Task.FromResult<AudioPostSynthesisAdjustmentProposal?>(null);

        double addedSeconds = insertionPoints.Count * pauseBytes / (double)wavInfo.ByteRate;
        logger.LogInformation(
            "[DurationStrategy:{Name}] Attempt {Attempt}/{MaxAttempts} inserted {Count} pause(s) x {PauseMs:F0}ms (+{Added:F3}s) for residual {Residual:F3}s.",
            Name,
            context.Attempt,
            context.MaxAttempts,
            insertionPoints.Count,
            pauseBytes * 1000.0 / wavInfo.ByteRate,
            addedSeconds,
            residual);

        return Task.FromResult<AudioPostSynthesisAdjustmentProposal?>(
            new AudioPostSynthesisAdjustmentProposal(
                adjusted,
                $"Inserted {insertionPoints.Count} short pause(s) (~+{addedSeconds:F2}s)."));
    }

    private static List<int> BuildInsertionPoints(
        IReadOnlyList<int> sentenceWordCounts,
        int dataSize,
        int boundaryCount,
        int blockAlign)
    {
        var insertionPoints = new List<int>(boundaryCount);
        int totalWords = sentenceWordCounts.Sum();
        if (totalWords <= 0)
            return insertionPoints;

        int runningWords = 0;
        for (int i = 0; i < boundaryCount; i++)
        {
            runningWords += sentenceWordCounts[i];
            double fraction = runningWords / (double)totalWords;
            int point = AudioMathHelper.AlignToBlockBoundary((int)Math.Round(dataSize * fraction), blockAlign);
            point = Math.Clamp(point, blockAlign, Math.Max(blockAlign, dataSize - blockAlign));

            if (insertionPoints.Count == 0 || insertionPoints[^1] != point)
                insertionPoints.Add(point);
        }

        return insertionPoints;
    }

    private static byte[] InsertSilenceAtOffsets(
        byte[] wav,
        WavInfo wavInfo,
        IReadOnlyList<int> offsets,
        int pauseBytes)
    {
        int dataStart = (int)wavInfo.DataPosition;
        if (dataStart <= 0 || dataStart + wavInfo.DataSize > wav.Length)
            return wav;

        byte[] originalData = new byte[wavInfo.DataSize];
        Buffer.BlockCopy(wav, dataStart, originalData, 0, wavInfo.DataSize);

        byte[] newData = new byte[originalData.Length + pauseBytes * offsets.Count];
        int srcIndex = 0;
        int dstIndex = 0;

        foreach (int offset in offsets.Order())
        {
            int clampedOffset = Math.Clamp(offset, srcIndex, originalData.Length);
            int copyLength = clampedOffset - srcIndex;
            if (copyLength > 0)
            {
                Buffer.BlockCopy(originalData, srcIndex, newData, dstIndex, copyLength);
                srcIndex += copyLength;
                dstIndex += copyLength;
            }

            dstIndex += pauseBytes; // zeros are already silence
        }

        if (srcIndex < originalData.Length)
            Buffer.BlockCopy(originalData, srcIndex, newData, dstIndex, originalData.Length - srcIndex);

        byte[] resized = new byte[dataStart + newData.Length];
        Buffer.BlockCopy(wav, 0, resized, 0, dataStart);
        Buffer.BlockCopy(newData, 0, resized, dataStart, newData.Length);

        using var ms = new MemoryStream(resized);
        using var writer = new BinaryWriter(ms);
        ms.Position = 4;
        writer.Write(resized.Length - 8);
        ms.Position = wavInfo.DataPosition - 4;
        writer.Write(newData.Length);

        return resized;
    }

    private static List<int> GetSentenceWordCounts(string script)
    {
        var sentences = SentenceBoundaryRegex()
            .Split(script)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        return sentences
            .Select(s => WordCounter.Count(s))
            .Where(count => count > 0)
            .ToList();
    }

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceBoundaryRegex();
}
