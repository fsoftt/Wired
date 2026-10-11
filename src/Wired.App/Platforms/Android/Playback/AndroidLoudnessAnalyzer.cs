using System.Runtime.InteropServices;
using Android.Media;
using Wired.Core.Abstractions;
using Wired.Core.Audio;

namespace Wired.App.Playback;

/// <summary>
/// Decodes a few seconds from several parts of the song with the platform decoders and measures
/// them: close enough to the whole song and much faster than decoding all of it.
/// </summary>
public sealed class AndroidLoudnessAnalyzer : ILoudnessAnalyzer
{
    private const int Segments = 8;
    private const long SegmentMicroseconds = 4_000_000;
    private const long TimeoutMicroseconds = 10_000;

    /// <summary>Safety net against decoders that never signal the end of a segment.</summary>
    private const int MaxIterationsPerSegment = 5_000;

    public Task<double?> MeasureAsync(string filePath, CancellationToken cancellationToken = default) =>
        Task.Run(() => Measure(filePath, cancellationToken), cancellationToken);

    private static double? Measure(string path, CancellationToken cancellationToken)
    {
        using var extractor = new MediaExtractor();
        extractor.SetDataSource(path);

        var (track, format) = FindAudioTrack(extractor);
        if (format is null)
            return null;

        extractor.SelectTrack(track);
        var mime = format.GetString(MediaFormat.KeyMime)!;
        var duration = format.ContainsKey(MediaFormat.KeyDuration) ? format.GetLong(MediaFormat.KeyDuration) : 0;

        using var codec = MediaCodec.CreateDecoderByType(mime);
        codec.Configure(format, null, null, MediaCodecConfigFlags.None);
        codec.Start();
        try
        {
            var decoder = new SegmentDecoder(codec, extractor, format);
            if (duration > Segments * SegmentMicroseconds * 2)
            {
                // Skip the very start and end (fades), spread the rest over the song.
                for (var i = 0; i < Segments; i++)
                    decoder.Decode((long)(duration * (0.05 + 0.9 * i / Segments)), SegmentMicroseconds, cancellationToken);
            }
            else
            {
                decoder.Decode(0, long.MaxValue / 2, cancellationToken);
            }

            return decoder.Meter?.Result;
        }
        finally
        {
            codec.Stop();
            codec.Release();
            extractor.Release();
        }
    }

    private static (int Track, MediaFormat? Format) FindAudioTrack(MediaExtractor extractor)
    {
        for (var i = 0; i < extractor.TrackCount; i++)
        {
            var format = extractor.GetTrackFormat(i);
            if (format.GetString(MediaFormat.KeyMime)?.StartsWith("audio/", StringComparison.Ordinal) == true)
                return (i, format);
        }

        return (-1, null);
    }

    private sealed class SegmentDecoder(MediaCodec codec, MediaExtractor extractor, MediaFormat inputFormat)
    {
        private readonly MediaCodec.BufferInfo _info = new();
        private bool _isFloat;

        public LoudnessMeter? Meter { get; private set; }

        public void Decode(long start, long length, CancellationToken cancellationToken)
        {
            extractor.SeekTo(start, MediaExtractorSeekTo.PreviousSync);
            codec.Flush();

            var end = start + length;
            var inputDone = false;
            for (var iteration = 0; iteration < MaxIterationsPerSegment; iteration++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!inputDone)
                    inputDone = FeedInput(end);

                var index = codec.DequeueOutputBuffer(_info, TimeoutMicroseconds);
                if (index == (int)MediaCodecInfoState.OutputFormatChanged)
                {
                    UseOutputFormat(codec.OutputFormat);
                }
                else if (index >= 0)
                {
                    if (_info.Size > 0 && _info.PresentationTimeUs >= start)
                        Measure(index);

                    codec.ReleaseOutputBuffer(index, false);
                    if ((_info.Flags & MediaCodecBufferFlags.EndOfStream) != 0)
                        return;
                }
            }
        }

        /// <returns>True once the end of the segment (or of the file) was sent.</returns>
        private bool FeedInput(long end)
        {
            var index = codec.DequeueInputBuffer(TimeoutMicroseconds);
            if (index < 0)
                return false;

            var buffer = codec.GetInputBuffer(index)!;
            var size = extractor.ReadSampleData(buffer, 0);
            if (size < 0 || extractor.SampleTime > end)
            {
                codec.QueueInputBuffer(index, 0, 0, 0, MediaCodecBufferFlags.EndOfStream);
                return true;
            }

            codec.QueueInputBuffer(index, 0, size, extractor.SampleTime, MediaCodecBufferFlags.None);
            extractor.Advance();
            return false;
        }

        private void UseOutputFormat(MediaFormat format)
        {
            _isFloat = OperatingSystem.IsAndroidVersionAtLeast(24)
                && format.ContainsKey(MediaFormat.KeyPcmEncoding)
                && format.GetInteger(MediaFormat.KeyPcmEncoding) == (int)Encoding.PcmFloat;
            Meter ??= new LoudnessMeter(format.GetInteger(MediaFormat.KeySampleRate), format.GetInteger(MediaFormat.KeyChannelCount));
        }

        private void Measure(int index)
        {
            if (Meter is null)
                UseOutputFormat(inputFormat);

            var buffer = codec.GetOutputBuffer(index)!;
            buffer.Position(_info.Offset);
            buffer.Limit(_info.Offset + _info.Size);
            var bytes = new byte[_info.Size];
            buffer.Get(bytes);

            if (_isFloat)
            {
                var floats = MemoryMarshal.Cast<byte, float>(bytes);
                var samples = new short[floats.Length];
                for (var i = 0; i < floats.Length; i++)
                    samples[i] = (short)Math.Clamp(floats[i] * 32767f, short.MinValue, short.MaxValue);
                Meter!.Add(samples);
            }
            else
            {
                Meter!.Add(MemoryMarshal.Cast<byte, short>(bytes));
            }
        }
    }
}
