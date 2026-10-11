using System.Net;
using System.Text;
using Wired.Core.Lyrics;
using Wired.Core.Playback;
using Wired.Infrastructure.Lyrics;
using Wired.Tests.Fakes;

namespace Wired.Tests;

public class LrcParserTests
{
    [Fact]
    public void Parses_lines_fractions_repeated_tags_and_offset()
    {
        const string lrc = """
            [ar:Someone]
            [offset:+500]
            [00:12.5]Uno
            [00:10.00]Cero
            [01:02.345][01:30.00]Coro
            [00:20.00]
            """;

        var lines = LrcParser.Parse(lrc);

        Assert.Equal(
            [
                new LyricLine(TimeSpan.FromSeconds(9.5), "Cero"),
                new LyricLine(TimeSpan.FromSeconds(12), "Uno"),
                new LyricLine(TimeSpan.FromSeconds(19.5), ""),
                new LyricLine(TimeSpan.FromMilliseconds(61_845), "Coro"),
                new LyricLine(TimeSpan.FromSeconds(89.5), "Coro"),
            ],
            lines);
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(10, 0)]
    [InlineData(14.9, 0)]
    [InlineData(15, 1)]
    [InlineData(999, 2)]
    public void Finds_the_line_being_sung(double seconds, int expectedIndex)
    {
        var lyrics = new Lyrics("[00:10.00]a\n[00:15.00]b\n[00:20.00]c", null);

        Assert.Equal(expectedIndex, lyrics.IndexAt(TimeSpan.FromSeconds(seconds)));
    }
}

public class TrackTitleCleanerTests
{
    [Theory]
    [InlineData("Ana Guerra - Mi Sueño (Official Video)", "AnaGuerraVEVO", "Ana Guerra", "Mi Sueño")]
    [InlineData("Mi Sueño", "Ana Guerra - Topic", "Ana Guerra", "Mi Sueño")]
    [InlineData("Bad Bunny - Tití Me Preguntó (Video Oficial) [4K]", "Bad Bunny", "Bad Bunny", "Tití Me Preguntó")]
    [InlineData("Shakira - Hips Don't Lie ft. Wyclef Jean", "shakiraVEVO", "Shakira", "Hips Don't Lie")]
    [InlineData("Despacito (Letra / Lyrics) | Luis Fonsi", "Some Channel", "Some Channel", "Despacito")]
    [InlineData("Queen – Bohemian Rhapsody (Remastered 2011)", "Queen Official", "Queen", "Bohemian Rhapsody")]
    public void Extracts_artist_and_title(string rawTitle, string channel, string artist, string title)
    {
        Assert.Equal((artist, title), TrackTitleCleaner.Clean(rawTitle, channel));
    }
}

public sealed class LrcLibLyricsProviderTests
{
    private readonly FakeHttp _http = new();
    private readonly LrcLibLyricsProvider _provider;

    public LrcLibLyricsProviderTests() =>
        _provider = new LrcLibLyricsProvider(new HttpClient(_http) { BaseAddress = new Uri(LrcLibLyricsProvider.BaseUrl) });

    [Fact]
    public async Task Uses_the_exact_match_when_available()
    {
        _http.Respond("/api/get", """{"trackName":"Mi Sueño","duration":200,"instrumental":false,"syncedLyrics":"[00:01.00]Hola"}""");

        var lyrics = await _provider.FindAsync(new LyricsQuery("Mi Sueño", "Ana Guerra", TimeSpan.FromSeconds(200)));

        Assert.True(lyrics!.IsSynced);
        Assert.Equal("Hola", lyrics.Lines[0].Text);
        Assert.Contains("duration=200", _http.Requests[0]);
    }

    [Fact]
    public async Task Falls_back_to_search_and_prefers_synced_lyrics_with_close_duration()
    {
        _http.Respond("/api/get", "", HttpStatusCode.BadRequest);
        _http.Respond("/api/search", """
            [
              {"trackName":"A","duration":200,"instrumental":false,"plainLyrics":"solo texto"},
              {"trackName":"B","duration":320,"instrumental":false,"syncedLyrics":"[00:01.00]version larga"},
              {"trackName":"C","duration":205,"instrumental":false,"syncedLyrics":"[00:01.00]buena"}
            ]
            """);

        var lyrics = await _provider.FindAsync(new LyricsQuery("Song", "Artist", TimeSpan.FromSeconds(200)));

        Assert.Equal("buena", lyrics!.Lines[0].Text);
    }

    [Fact]
    public async Task Returns_null_when_nothing_matches()
    {
        _http.Respond("/api/get", "", HttpStatusCode.NotFound);
        _http.Respond("/api/search", "[]");

        Assert.Null(await _provider.FindAsync(new LyricsQuery("Song", "Artist", TimeSpan.FromSeconds(200))));
        Assert.Equal(3, _http.Requests.Count); // exact, search by fields, free-text search
    }

    private sealed class FakeHttp : HttpMessageHandler
    {
        private readonly Dictionary<string, (string Body, HttpStatusCode Status)> _responses = [];

        public List<string> Requests { get; } = [];

        public void Respond(string path, string body, HttpStatusCode status = HttpStatusCode.OK) => _responses[path] = (body, status);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            var (body, status) = _responses.GetValueOrDefault(request.RequestUri.AbsolutePath, ("", HttpStatusCode.NotFound));
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}

public sealed class LyricsServiceTests : IDisposable
{
    private readonly TempPaths _paths = new();

    [Fact]
    public async Task Caches_lyrics_on_disk_for_offline_use()
    {
        var provider = new CountingProvider(new Lyrics("[00:01.00]hola", null));
        var item = PlaybackItem.FromStream(TestData.RemoteTrack("v1"));

        await new LyricsService(provider, new FileLyricsCache(_paths)).GetAsync(item);
        var offline = await new LyricsService(provider, new FileLyricsCache(_paths)).GetAsync(item);

        Assert.Equal("hola", offline!.Lines[0].Text);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task Streamed_and_downloaded_versions_share_lyrics()
    {
        var local = TestData.LocalTrack("song");
        var remote = new Core.Models.RemoteTrack(local.SourceId, local.RemoteId, local.Title, local.Artist, null, null);

        Assert.Equal(
            LyricsService.KeyFor(PlaybackItem.FromLocal(local)),
            LyricsService.KeyFor(PlaybackItem.FromStream(remote)));
    }

    public void Dispose() => _paths.Dispose();

    private sealed class CountingProvider(Lyrics? result) : ILyricsProvider
    {
        public int Calls { get; private set; }

        public Task<Lyrics?> FindAsync(LyricsQuery query, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }
}
