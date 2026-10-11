using Wired.Core.Abstractions;
using Wired.Core.Audio;
using Wired.Core.Collections;
using Wired.Core.Downloads;
using Wired.Core.Enrichment;
using Wired.Core.Import;
using Wired.Core.Models;
using Wired.Core.Playback;
using Wired.Core.Plugins;
using Wired.Core.Storage;
using Wired.Core.UseCases;
using Wired.Infrastructure.Collections;
using Wired.Infrastructure.Media;
using Wired.Tests.Fakes;

namespace Wired.Tests;

public sealed class LoudnessMeterTests
{
    private const int SampleRate = 44100;

    /// <summary>dBFS of a sine: its mean square is amplitude^2 / 2, so 3.01 dB below the peak level.</summary>
    private static double ExpectedSineDb(double amplitude) => 20 * Math.Log10(amplitude / 32768) - 3.0103;

    [Fact]
    public void Full_scale_square_wave_measures_about_0_db()
    {
        var samples = new short[SampleRate * 2];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = (i / 50) % 2 == 0 ? (short)32767 : (short)-32767;
        var meter = new LoudnessMeter(SampleRate, 1);

        meter.Add(samples);

        Assert.InRange(meter.Result!.Value, -0.1, 0.1);
    }

    [Theory]
    [InlineData(16384)]
    [InlineData(1000)]
    public void Sine_measures_its_rms_level(double amplitude)
    {
        var meter = new LoudnessMeter(SampleRate, 1);

        meter.Add(Sine(SampleRate, 1, seconds: 3, amplitude));

        var expected = ExpectedSineDb(amplitude);
        Assert.InRange(meter.Result!.Value, expected - 0.1, expected + 0.1);
    }

    [Fact]
    public void Silence_returns_null()
    {
        var meter = new LoudnessMeter(SampleRate, 1);

        meter.Add(new short[SampleRate * 2]);

        Assert.Null(meter.Result);
    }

    [Fact]
    public void Nothing_fed_returns_null()
    {
        Assert.Null(new LoudnessMeter(SampleRate, 2).Result);
    }

    [Fact]
    public void A_long_silent_intro_does_not_change_the_result()
    {
        var tone = Sine(SampleRate, 1, seconds: 4, amplitude: 8192);
        var withoutIntro = new LoudnessMeter(SampleRate, 1);
        var withIntro = new LoudnessMeter(SampleRate, 1);

        withoutIntro.Add(tone);
        withIntro.Add(new short[SampleRate * 4]);
        withIntro.Add(tone);

        Assert.Equal(withoutIntro.Result!.Value, withIntro.Result!.Value, precision: 6);
    }

    [Fact]
    public void Stereo_and_mono_give_the_same_result_for_the_same_signal()
    {
        var mono = Sine(SampleRate, 1, seconds: 3, amplitude: 4096);
        var stereo = Sine(SampleRate, 2, seconds: 3, amplitude: 4096);
        var monoMeter = new LoudnessMeter(SampleRate, 1);
        var stereoMeter = new LoudnessMeter(SampleRate, 2);

        monoMeter.Add(mono);
        stereoMeter.Add(stereo);

        Assert.Equal(monoMeter.Result!.Value, stereoMeter.Result!.Value, precision: 6);
    }

    [Fact]
    public void Samples_split_across_many_adds_give_the_same_result_as_one_add()
    {
        var signal = Sine(SampleRate, 2, seconds: 3, amplitude: 2048);
        var whole = new LoudnessMeter(SampleRate, 2);
        var chunked = new LoudnessMeter(SampleRate, 2);
        int[] chunkSizes = [1, 7, 1000, 17639, 17641, 3, 35279];

        whole.Add(signal);
        var position = 0;
        var index = 0;
        while (position < signal.Length)
        {
            var size = Math.Min(chunkSizes[index++ % chunkSizes.Length], signal.Length - position);
            chunked.Add(signal.AsSpan(position, size));
            position += size;
        }

        Assert.Equal(whole.Result!.Value, chunked.Result!.Value, precision: 9);
    }

    /// <summary>Interleaved 16-bit sine, the same signal on every channel.</summary>
    private static short[] Sine(int sampleRate, int channels, double seconds, double amplitude, double frequency = 1000)
    {
        var frames = (int)Math.Round(sampleRate * seconds);
        var samples = new short[frames * channels];
        for (var frame = 0; frame < frames; frame++)
        {
            var value = (short)Math.Round(amplitude * Math.Sin(2 * Math.PI * frequency * frame / sampleRate));
            for (var channel = 0; channel < channels; channel++)
                samples[frame * channels + channel] = value;
        }

        return samples;
    }
}

public sealed class LoudnessNormalizationTests
{
    [Fact]
    public void Unknown_loudness_gets_no_gain()
    {
        Assert.Equal(0, LoudnessNormalization.GainFor(null));
    }

    [Fact]
    public void Track_at_the_target_level_gets_no_gain()
    {
        Assert.Equal(0, LoudnessNormalization.GainFor(LoudnessNormalization.TargetDb));
    }

    [Fact]
    public void Quiet_track_is_boosted_by_the_difference_to_target()
    {
        Assert.Equal(4, LoudnessNormalization.GainFor(-18), precision: 9);
    }

    [Fact]
    public void Very_quiet_track_is_boosted_only_up_to_the_maximum()
    {
        Assert.Equal(LoudnessNormalization.MaxBoostDb, LoudnessNormalization.GainFor(-30), precision: 9);
    }

    [Fact]
    public void Loud_track_is_cut_by_the_difference_to_target()
    {
        Assert.Equal(-8, LoudnessNormalization.GainFor(-6), precision: 9);
    }

    [Fact]
    public void Very_loud_track_is_cut_only_down_to_the_maximum()
    {
        Assert.Equal(LoudnessNormalization.MaxCutDb, LoudnessNormalization.GainFor(0), precision: 9);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_loudness_gets_no_gain(double loudness)
    {
        Assert.Equal(0, LoudnessNormalization.GainFor(loudness));
    }

    [Theory]
    [InlineData(-60)]
    [InlineData(-14)]
    [InlineData(-3)]
    [InlineData(10)]
    public void Gain_always_stays_within_the_limits(double loudness)
    {
        var gain = LoudnessNormalization.GainFor(loudness);

        Assert.InRange(gain, LoudnessNormalization.MaxCutDb, LoudnessNormalization.MaxBoostDb);
    }

    [Fact]
    public void Amplitude_of_minus_6_dB_is_about_one_half()
    {
        Assert.Equal(0.5, LoudnessNormalization.ToAmplitude(-6.0206), precision: 4);
    }

    [Fact]
    public void Amplitude_of_zero_dB_is_one()
    {
        Assert.Equal(1, LoudnessNormalization.ToAmplitude(0), precision: 9);
    }
}

public sealed class PlaybackLoudnessTests
{
    private readonly FakeAudioPlayer _player = new();
    private readonly PlaybackService _service;

    public PlaybackLoudnessTests() => _service = TestPlayback.Create(_player);

    [Theory]
    [InlineData(-20, 6)]
    [InlineData(-5, -9)]
    [InlineData(0, -12)]
    public async Task Local_track_plays_with_the_gain_for_its_loudness(double loudness, double expectedGain)
    {
        var track = PlaybackItem.FromLocal(TestData.LocalTrack("a") with { LoudnessDb = loudness });

        await _service.PlayAsync([track]);

        Assert.Equal(expectedGain, _player.GainDb, precision: 9);
        Assert.Equal(LoudnessNormalization.GainFor(loudness), _player.GainDb, precision: 9);
    }

    [Fact]
    public async Task Local_track_without_loudness_plays_at_unity_gain()
    {
        // Start with a gain applied, so the test shows the gain is reset rather than left over.
        await _service.PlayAsync([PlaybackItem.FromLocal(TestData.LocalTrack("loud") with { LoudnessDb = -5 })]);
        Assert.NotEqual(0, _player.GainDb);

        await _service.PlayAsync([PlaybackItem.FromLocal(TestData.LocalTrack("plain"))]);

        Assert.Equal(0, _player.GainDb);
    }

    [Fact]
    public async Task Streamed_item_plays_at_unity_gain()
    {
        await _service.PlayAsync([PlaybackItem.FromLocal(TestData.LocalTrack("loud") with { LoudnessDb = -5 })]);
        Assert.NotEqual(0, _player.GainDb);

        await _service.PlayAsync([PlaybackItem.FromStream(TestData.RemoteTrack("x"))]);

        Assert.Equal("stream:x", _player.LoadedStream);
        Assert.Equal(0, _player.GainDb);
    }
}

public sealed class LibraryLoudnessTests : IDisposable
{
    private readonly TempPaths _paths = new();

    [Fact]
    public async Task Enrich_stores_the_measured_loudness()
    {
        var library = new InMemoryLibrary();
        var track = AddTrack(library, "a");
        var analyzer = new FakeAnalyzer(-18.5);
        var service = CreateService(library, analyzer);

        await service.EnrichAsync(track.Id);

        Assert.Equal(-18.5, Assert.Single(library.Tracks).LoudnessDb);
        Assert.Equal(1, analyzer.Calls);
    }

    [Fact]
    public async Task Undecodable_file_gets_the_target_loudness()
    {
        var library = new InMemoryLibrary();
        var track = AddTrack(library, "a");
        var analyzer = new FakeAnalyzer(null);
        var service = CreateService(library, analyzer);

        await service.EnrichAsync(track.Id);

        Assert.Equal(LoudnessNormalization.TargetDb, Assert.Single(library.Tracks).LoudnessDb);
    }

    [Fact]
    public async Task Analyzer_failure_gets_the_target_loudness()
    {
        var library = new InMemoryLibrary();
        var track = AddTrack(library, "a");
        var service = CreateService(library, new FakeAnalyzer(error: new IOException("decoder crashed")));

        await service.EnrichAsync(track.Id);

        Assert.Equal(LoudnessNormalization.TargetDb, Assert.Single(library.Tracks).LoudnessDb);
    }

    [Fact]
    public async Task Measured_track_is_not_measured_again()
    {
        var library = new InMemoryLibrary();
        var track = AddTrack(library, "a");
        var analyzer = new FakeAnalyzer(-18.5);
        var service = CreateService(library, analyzer);

        await service.EnrichAsync(track.Id);
        await service.EnrichAsync(track.Id);

        Assert.Equal(1, analyzer.Calls);
        Assert.Equal(-18.5, Assert.Single(library.Tracks).LoudnessDb);
    }

    [Fact]
    public async Task Already_measured_loudness_is_kept_without_calling_the_analyzer()
    {
        var library = new InMemoryLibrary();
        var track = AddTrack(library, "a", loudness: -10);
        var analyzer = new FakeAnalyzer(-18.5);
        var service = CreateService(library, analyzer);

        await service.EnrichAsync(track.Id);

        Assert.Equal(0, analyzer.Calls);
        Assert.Equal(-10, Assert.Single(library.Tracks).LoudnessDb);
    }

    [Fact]
    public async Task Backfill_measures_an_enriched_track_that_has_no_loudness()
    {
        var library = new InMemoryLibrary();
        AddTrack(library, "a", enriched: true);
        var analyzer = new FakeAnalyzer(-22);
        var service = CreateService(library, analyzer);

        await service.BackfillAsync();

        var track = Assert.Single(library.Tracks);
        Assert.Equal(-22, track.LoudnessDb);
        Assert.True(track.IsEnriched);
        Assert.Equal(1, analyzer.Calls);
    }

    private LocalTrack AddTrack(InMemoryLibrary library, string name, bool enriched = false, double? loudness = null)
    {
        Directory.CreateDirectory(_paths.MusicDirectory);
        var file = Path.Combine(_paths.MusicDirectory, $"{name}.m4a");
        File.WriteAllText(file, "audio");
        var track = TestData.LocalTrack(name, file) with { IsEnriched = enriched, LoudnessDb = loudness };
        library.Tracks.Add(track);
        return track;
    }

    private LibraryEnrichmentService CreateService(InMemoryLibrary library, ILoudnessAnalyzer analyzer)
    {
        var catalog = new PluginCatalog([new FakePlugin()]);
        var clock = new FixedClock(DateTimeOffset.Now);
        var downloads = new DownloadQueue(new DownloadTrackUseCase(catalog, library, _paths, clock));
        var playlists = new PlaylistService(new JsonPlaylistRepository(_paths), library, clock);
        var likes = new LikesService(new JsonLikedTracksRepository(_paths), library);
        var imports = new PlaylistImportService(
            new JsonImportSessionRepository(_paths), new SearchTracksUseCase(catalog), downloads, playlists, likes, library, clock);
        var artwork = new FileArtworkStore(_paths, new HttpClient(new NoContentHandler()));
        return new LibraryEnrichmentService(library, artwork, new TagLibAudioTagger(), downloads, imports, analyzer);
    }

    public void Dispose() => _paths.Dispose();

    private sealed class FakeAnalyzer(double? result = null, Exception? error = null) : ILoudnessAnalyzer
    {
        public int Calls { get; private set; }

        public Task<double?> MeasureAsync(string filePath, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (error is not null)
                throw error;
            return Task.FromResult(result);
        }
    }

    private sealed class NoContentHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }
}
