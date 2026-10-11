namespace Wired.Core.Abstractions;

/// <summary>Decodes an audio file and measures its loudness. Implemented per platform.</summary>
public interface ILoudnessAnalyzer
{
    /// <returns>Loudness as measured by <see cref="Audio.LoudnessMeter"/>, or null if the file cannot be decoded.</returns>
    Task<double?> MeasureAsync(string filePath, CancellationToken cancellationToken = default);
}
