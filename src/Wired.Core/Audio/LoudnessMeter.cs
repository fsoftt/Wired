namespace Wired.Core.Audio;

/// <summary>
/// Measures how loud a piece of audio sounds, from its PCM samples: the average energy of 400 ms
/// blocks, skipping near-silent ones (intros, pauses) so they do not make a song look quiet.
/// A simplified version of the gated loudness used by broadcast standards; good enough to even
/// out songs from different sources.
/// </summary>
public sealed class LoudnessMeter(int sampleRate, int channels)
{
    /// <summary>Blocks quieter than this (dBFS) are silence.</summary>
    public const double SilenceGateDb = -50;

    private readonly int _blockSize = Math.Max(1, sampleRate * channels * 2 / 5);
    private double _blockEnergy;
    private int _blockCount;
    private double _totalEnergy;
    private int _loudBlocks;

    /// <summary>Adds interleaved 16-bit samples.</summary>
    public void Add(ReadOnlySpan<short> samples)
    {
        foreach (var sample in samples)
        {
            var value = sample / 32768.0;
            _blockEnergy += value * value;
            if (++_blockCount == _blockSize)
                CloseBlock();
        }
    }

    /// <summary>Loudness in dBFS (a full-scale square wave is 0), or null when everything was silence.</summary>
    public double? Result => _loudBlocks == 0 ? null : ToDb(_totalEnergy / _loudBlocks);

    private void CloseBlock()
    {
        var meanSquare = _blockEnergy / _blockCount;
        if (ToDb(meanSquare) > SilenceGateDb)
        {
            _totalEnergy += meanSquare;
            _loudBlocks++;
        }

        _blockEnergy = 0;
        _blockCount = 0;
    }

    private static double ToDb(double meanSquare) => meanSquare <= 0 ? double.NegativeInfinity : 10 * Math.Log10(meanSquare);
}
