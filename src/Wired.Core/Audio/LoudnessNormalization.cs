namespace Wired.Core.Audio;

/// <summary>Turns a measured loudness into the gain that brings the song to a common level.</summary>
public static class LoudnessNormalization
{
    /// <summary>Common level, in the units of <see cref="LoudnessMeter"/>. Typical of today's music.</summary>
    public const double TargetDb = -14;

    /// <summary>Quiet songs are boosted at most this much: more would bring up noise and distort.</summary>
    public const double MaxBoostDb = 6;

    public const double MaxCutDb = -12;

    /// <returns>Gain in dB (negative makes it quieter); 0 when the loudness is unknown.</returns>
    public static double GainFor(double? loudnessDb) =>
        loudnessDb is { } measured && double.IsFinite(measured)
            ? Math.Clamp(TargetDb - measured, MaxCutDb, MaxBoostDb)
            : 0;

    /// <summary>Linear amplitude factor of a gain in dB.</summary>
    public static double ToAmplitude(double gainDb) => Math.Pow(10, gainDb / 20);
}
