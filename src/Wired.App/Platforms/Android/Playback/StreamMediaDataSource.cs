using Android.Media;

namespace Wired.App.Playback;

/// <summary>
/// Feeds <see cref="MediaPlayer"/> from a seekable in-memory .NET stream.
/// Called from native threads: it must never throw, so failures are reported as end of stream.
/// </summary>
public sealed class StreamMediaDataSource(System.IO.Stream stream) : MediaDataSource
{
    private const int EndOfStream = -1;

    private readonly Lock _lock = new();

    public override long Size
    {
        get
        {
            lock (_lock)
            {
                try
                {
                    return stream.Length;
                }
                catch (Exception)
                {
                    return EndOfStream;
                }
            }
        }
    }

    public override int ReadAt(long position, byte[]? buffer, int offset, int size)
    {
        if (buffer is null || size == 0)
            return 0;

        lock (_lock)
        {
            try
            {
                if (position >= stream.Length)
                    return EndOfStream;

                stream.Position = position;

                var read = 0;
                while (read < size)
                {
                    var count = stream.Read(buffer, offset + read, size - read);
                    if (count == 0)
                        break;
                    read += count;
                }

                return read == 0 ? EndOfStream : read;
            }
            catch (Exception)
            {
                return EndOfStream;
            }
        }
    }

    public override void Close()
    {
        lock (_lock)
            stream.Dispose();
    }
}
