using Wired.Core.Abstractions;
using Wired.Core.Collections;
using Wired.Core.Downloads;
using Wired.Core.Enrichment;
using Wired.Core.Import;
using Wired.Core.Models;
using Wired.Core.Plugins;
using Wired.Core.UseCases;
using Wired.Infrastructure.Collections;
using Wired.Presentation.Collections;
using Wired.Presentation.Formatting;
using Wired.Presentation.Services;
using Wired.Presentation.ViewModels;
using Wired.Tests.Fakes;

namespace Wired.Tests;

/// <summary>Library sort orders, the letter index, and the remembered sort choice.</summary>
public sealed class LibrarySortTests : IDisposable
{
    private readonly TempPaths _paths = new();

    public void Dispose() => _paths.Dispose();

    // ---- SortKey ----

    [Theory]
    [InlineData("¡Hola", "HOLA")]
    [InlineData("Él", "EL")]
    [InlineData("ñandú", "NANDU")]
    [InlineData("💫 3 Horas", "3 HORAS")]
    [InlineData("   hola", "HOLA")]
    [InlineData("¿¡!", "")]
    [InlineData("", "")]
    public void SortKey_For_strips_leading_symbols_removes_accents_and_uppercases(string text, string expected) =>
        Assert.Equal(expected, SortKey.For(text));

    [Theory]
    [InlineData("HOLA", "H")]
    [InlineData("NANDU", "N")]
    [InlineData("3 HORAS", "#")]
    [InlineData("", "#")]
    [InlineData("ØRSTED", "#")]
    public void SortKey_LetterOf_gives_the_letter_or_hash(string key, string expected) =>
        Assert.Equal(expected, SortKey.LetterOf(key));

    [Fact]
    public void SortKey_groups_accented_and_symbol_led_titles_under_their_base_letter()
    {
        Assert.Equal("H", SortKey.LetterOf(SortKey.For("¡Hola")));
        Assert.Equal("E", SortKey.LetterOf(SortKey.For("Él")));
        Assert.Equal("#", SortKey.LetterOf(SortKey.For("💫 3 Horas")));
    }

    // ---- Default and title/artist ordering ----

    [Fact]
    public async Task Default_order_is_recent_in_library_order_with_no_index()
    {
        var scene = await OpenAsync([Song("Zeta"), Song("Alpha"), Song("3 Horas")]);

        Assert.Equal(LibrarySortOrder.Recent, scene.Vm.SortOrder);
        Assert.Equal(new[] { "Zeta", "Alpha", "3 Horas" }, Titles(scene.Vm));
        Assert.Empty(scene.Vm.IndexLetters);
        Assert.False(scene.Vm.IsIndexVisible);
        Assert.Equal("Recientes", scene.Vm.SortLabel);
    }

    [Fact]
    public async Task Title_order_ignores_accents_case_and_leading_symbols()
    {
        var scene = await OpenAsync([
            Song("Zeta"),
            Song("ñandú"),
            Song("hola", "Bea"),
            Song("¡Hola", "Ana"),
            Song("Él"),
            Song("💫 3 Horas"),
            Song("Alpha"),
        ]);

        await SortByAsync(scene, 1);

        Assert.Equal(LibrarySortOrder.Title, scene.Vm.SortOrder);
        Assert.Equal(new[] { "💫 3 Horas", "Alpha", "Él", "¡Hola", "hola", "ñandú", "Zeta" }, Titles(scene.Vm));
    }

    [Fact]
    public async Task Title_ties_are_broken_by_artist()
    {
        var scene = await OpenAsync([Song("Hola", "Zoe"), Song("¡Hola", "Ana")]);

        await SortByAsync(scene, 1);

        Assert.Equal(new[] { "Ana", "Zoe" }, Artists(scene.Vm));
    }

    [Fact]
    public async Task Artist_order_groups_by_artist_then_title()
    {
        var scene = await OpenAsync([
            Song("Zeta", "Bravo"),
            Song("Alpha", "Bravo"),
            Song("Sol", "Ámbar"),
            Song("Ola", "alpha band"),
        ]);

        await SortByAsync(scene, 2);

        Assert.Equal(LibrarySortOrder.Artist, scene.Vm.SortOrder);
        Assert.Equal(new[] { "Ola", "Sol", "Alpha", "Zeta" }, Titles(scene.Vm));
        Assert.Equal("Artista (A-Z)", scene.Vm.SortLabel);
    }

    [Fact]
    public async Task Filtering_keeps_the_sorted_order_and_clearing_restores_all_rows()
    {
        var scene = await OpenAsync([Song("Zeta"), Song("Alpha"), Song("Beta"), Song("Theta")]);
        await SortByAsync(scene, 1);

        scene.Vm.Filter = "eta";
        Assert.Equal(new[] { "Beta", "Theta", "Zeta" }, Titles(scene.Vm));

        scene.Vm.Filter = string.Empty;
        Assert.Equal(new[] { "Alpha", "Beta", "Theta", "Zeta" }, Titles(scene.Vm));
    }

    // ---- Changing and remembering the sort ----

    [Fact]
    public async Task Choosing_a_sort_offers_the_three_options_and_saves_it()
    {
        var scene = await OpenAsync([Song("Alpha")]);

        await SortByAsync(scene, 2);

        Assert.Equal(new[] { "Recientes", "Título (A-Z)", "Artista (A-Z)" }, scene.Dialogs.Offered);
        Assert.Equal(LibrarySortOrder.Artist, scene.Vm.SortOrder);
        Assert.Equal("Artist", scene.Prefs.Get("library.sort"));
        Assert.Equal(new[] { ("library.sort", "Artist") }, scene.Prefs.Writes);
    }

    [Fact]
    public async Task A_new_view_model_restores_the_saved_sort()
    {
        var prefs = new MemoryPreferences();
        var library = new InMemoryLibrary();
        library.Tracks.AddRange([Song("Zeta", "Bravo"), Song("Alpha", "Ana")]);

        var first = CreateViewModel(library, new ChoosingDialogs { NextChoice = 1 }, prefs);
        await first.ChangeSortCommand.ExecuteAsync(null);

        var second = CreateViewModel(library, new ChoosingDialogs(), prefs);
        await second.LoadCommand.ExecuteAsync(null);

        Assert.Equal(LibrarySortOrder.Title, second.SortOrder);
        Assert.Equal(new[] { "Alpha", "Zeta" }, Titles(second));
    }

    [Fact]
    public void A_stored_sort_name_is_read_on_construction()
    {
        var prefs = new MemoryPreferences();
        prefs.Set("library.sort", "Title");

        var vm = CreateViewModel(new InMemoryLibrary(), new ChoosingDialogs(), prefs);

        Assert.Equal(LibrarySortOrder.Title, vm.SortOrder);
    }

    [Fact]
    public async Task Cancelling_the_sort_dialog_changes_nothing()
    {
        var scene = await OpenAsync([Song("Zeta"), Song("Alpha")]);

        await SortByAsync(scene, null);

        Assert.Equal(LibrarySortOrder.Recent, scene.Vm.SortOrder);
        Assert.Equal(new[] { "Zeta", "Alpha" }, Titles(scene.Vm));
        Assert.Empty(scene.Prefs.Writes);
    }

    // ---- Letter index ----

    [Fact]
    public async Task Index_letters_are_the_distinct_visible_letters_in_order()
    {
        var scene = await OpenAsync([Song("Zeta"), Song("Bravo"), Song("Alpha"), Song("3 Horas"), Song("Ánimo"), Song("Beta")]);

        await SortByAsync(scene, 1);

        Assert.Equal(new[] { "#", "A", "B", "Z" }, scene.Vm.IndexLetters);
    }

    [Fact]
    public async Task Index_letters_are_empty_in_recent_order_and_return_when_sorted_again()
    {
        var scene = await OpenAsync([Song("Zeta"), Song("Alpha")]);

        await SortByAsync(scene, 1);
        Assert.Equal(new[] { "A", "Z" }, scene.Vm.IndexLetters);

        await SortByAsync(scene, 0);
        Assert.Empty(scene.Vm.IndexLetters);
    }

    [Fact]
    public async Task Index_is_visible_only_with_at_least_twenty_visible_tracks()
    {
        var nineteen = Enumerable.Range(1, 18).Select(i => Song($"Alpha {i:00}")).Append(Song("Beta")).ToArray();
        var below = await OpenAsync(nineteen);
        await SortByAsync(below, 1);
        Assert.Equal(19, below.Vm.Tracks.Count);
        Assert.Equal(new[] { "A", "B" }, below.Vm.IndexLetters);
        Assert.False(below.Vm.IsIndexVisible);

        var twenty = await OpenAsync([.. nineteen, Song("Beta 2")]);
        await SortByAsync(twenty, 1);
        Assert.Equal(20, twenty.Vm.Tracks.Count);
        Assert.True(twenty.Vm.IsIndexVisible);
    }

    [Fact]
    public async Task Index_is_hidden_when_every_track_starts_with_the_same_letter()
    {
        var scene = await OpenAsync(Enumerable.Range(1, 25).Select(i => Song($"Track {i:00}")).ToArray());

        await SortByAsync(scene, 1);

        Assert.Equal(new[] { "T" }, scene.Vm.IndexLetters);
        Assert.False(scene.Vm.IsIndexVisible);
    }

    [Fact]
    public async Task Index_visibility_follows_the_filter()
    {
        var alphas = Enumerable.Range(1, 20).Select(i => Song($"Alpha {i:00}"));
        var betas = Enumerable.Range(1, 5).Select(i => Song($"Beta {i:00}"));
        var scene = await OpenAsync(alphas.Concat(betas).ToArray());
        await SortByAsync(scene, 1);
        Assert.True(scene.Vm.IsIndexVisible);

        scene.Vm.Filter = "Beta";

        Assert.Equal(5, scene.Vm.Tracks.Count);
        Assert.Equal(new[] { "B" }, scene.Vm.IndexLetters);
        Assert.False(scene.Vm.IsIndexVisible);
    }

    [Fact]
    public async Task IndexOfLetter_points_to_the_first_visible_row_of_each_letter()
    {
        var scene = await OpenAsync([Song("Zeta"), Song("Beta"), Song("Ánimo"), Song("Alpha"), Song("Bravo"), Song("3 Horas")]);

        await SortByAsync(scene, 1);

        Assert.Equal(0, scene.Vm.IndexOfLetter("#"));
        Assert.Equal(1, scene.Vm.IndexOfLetter("A"));
        Assert.Equal(3, scene.Vm.IndexOfLetter("B"));
        Assert.Equal(5, scene.Vm.IndexOfLetter("Z"));
        Assert.Equal(-1, scene.Vm.IndexOfLetter("Q"));
    }

    [Fact]
    public async Task Rows_with_the_same_index_letter_are_contiguous_when_titles_start_with_non_latin_letters()
    {
        // "Ørsted" and "Жизнь" are index "#" like "3 Horas", but they sort after "Z" in ordinal order.
        var scene = await OpenAsync([Song("Alpha"), Song("Zeta"), Song("Жизнь"), Song("Ørsted"), Song("3 Horas")]);

        await SortByAsync(scene, 1);

        var rowLetters = scene.Vm.Tracks.Select(t => SortKey.LetterOf(SortKey.For(t.Title))).ToList();
        var runs = rowLetters.Where((letter, i) => i == 0 || letter != rowLetters[i - 1]).ToList();
        Assert.Equal(rowLetters.Distinct().ToList(), runs);
    }

    [Fact]
    public void Stored_sort_value_that_is_not_a_known_order_falls_back_to_recent()
    {
        var prefs = new MemoryPreferences();
        prefs.Set("library.sort", "42");

        var vm = CreateViewModel(new InMemoryLibrary(), new ChoosingDialogs(), prefs);

        Assert.Equal(LibrarySortOrder.Recent, vm.SortOrder);
        Assert.Equal("Recientes", vm.SortLabel);
    }

    // ---- Harness ----

    private sealed class Scene
    {
        public required InMemoryLibrary Library { get; init; }
        public required LibraryViewModel Vm { get; init; }
        public required ChoosingDialogs Dialogs { get; init; }
        public required MemoryPreferences Prefs { get; init; }
    }

    private async Task<Scene> OpenAsync(LocalTrack[] tracks, MemoryPreferences? prefs = null)
    {
        var library = new InMemoryLibrary();
        library.Tracks.AddRange(tracks);
        var dialogs = new ChoosingDialogs();
        var preferences = prefs ?? new MemoryPreferences();
        var vm = CreateViewModel(library, dialogs, preferences);
        await vm.LoadCommand.ExecuteAsync(null);
        return new Scene { Library = library, Vm = vm, Dialogs = dialogs, Prefs = preferences };
    }

    private static async Task SortByAsync(Scene scene, int? choice)
    {
        scene.Dialogs.NextChoice = choice;
        await scene.Vm.ChangeSortCommand.ExecuteAsync(null);
    }

    private static LocalTrack Song(string title, string artist = "Artist") =>
        TestData.LocalTrack(title) with { Artist = artist };

    private static List<string> Titles(LibraryViewModel vm) => vm.Tracks.Select(t => t.Title).ToList();

    private static List<string> Artists(LibraryViewModel vm) => vm.Tracks.Select(t => t.Artist).ToList();

    private LibraryViewModel CreateViewModel(InMemoryLibrary library, IDialogService dialogs, IPreferenceStore preferences)
    {
        var catalog = new PluginCatalog([new FakePlugin()]);
        var clock = new FixedClock(DateTimeOffset.Now);
        var downloads = new DownloadQueue(new DownloadTrackUseCase(catalog, library, _paths, clock));
        var playlistRepository = new JsonPlaylistRepository(_paths);
        var likedRepository = new JsonLikedTracksRepository(_paths);
        var playlists = new PlaylistService(playlistRepository, library, clock);
        var likes = new LikesService(likedRepository, library);
        var playback = TestPlayback.Create(new FakeAudioPlayer(), new FakePlugin());
        var actions = new TrackCollectionActions(likes, playlists, new QueueActions(playback, dialogs), dialogs);
        var imports = new PlaylistImportService(
            new JsonImportSessionRepository(_paths), new SearchTracksUseCase(catalog), downloads, playlists, likes, library, clock);
        var enrichment = new LibraryEnrichmentService(library, new SortArtworkStore(), new SortAudioTagger(), downloads, imports);

        return new LibraryViewModel(
            library,
            new RemoveTrackUseCase(library, playlists, likes),
            new RecoverMusicUseCase(library, _paths),
            new CollectionsBackupService(playlistRepository, playlists, likedRepository, likes, library, _paths),
            new SortMusicFolder(),
            playback,
            likes,
            actions,
            downloads,
            dialogs,
            new ImmediateDispatcher(),
            new CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger(),
            enrichment,
            preferences: preferences);
    }

    private sealed class ChoosingDialogs : IDialogService
    {
        /// <summary>Index returned by the next ChooseAsync; null simulates cancel.</summary>
        public int? NextChoice { get; set; }

        public IReadOnlyList<string> Offered { get; private set; } = [];

        public Task<int?> ChooseAsync(string title, string cancel, IReadOnlyList<string> options)
        {
            Offered = options.ToList();
            return Task.FromResult(NextChoice);
        }

        public Task<bool> ConfirmAsync(string title, string message, string accept, string cancel) => Task.FromResult(false);

        public Task<string?> PromptAsync(string title, string message, string accept, string cancel, string? initialValue = null, int maxLength = -1) =>
            Task.FromResult<string?>(null);

        public Task ShowToastAsync(string message) => Task.CompletedTask;

        public Task ShowSnackbarAsync(string message, string actionText, Func<Task> action) => Task.CompletedTask;

        public Task ShowErrorAsync(string title, string message, string? report = null) => Task.CompletedTask;
    }

    private sealed class MemoryPreferences : IPreferenceStore
    {
        private readonly Dictionary<string, string> _values = new();

        public List<(string Key, string Value)> Writes { get; } = [];

        public string? Get(string key) => _values.GetValueOrDefault(key);

        public void Set(string key, string value)
        {
            _values[key] = value;
            Writes.Add((key, value));
        }
    }

    private sealed class SortArtworkStore : IArtworkStore
    {
        public string? Find(string key) => null;

        public Task<string?> SaveFromUrlAsync(string key, string url, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<string> SaveAsync(string key, byte[] image, CancellationToken cancellationToken = default) =>
            Task.FromResult(key);
    }

    private sealed class SortAudioTagger : IAudioTagger
    {
        public AudioTags? Read(string filePath) => null;

        public void Write(string filePath, string title, IReadOnlyList<string> artists, string? album, byte[]? cover)
        {
        }
    }

    private sealed class SortMusicFolder : IMusicFolderAccess
    {
        public string DisplayLocation => "Música/Wired";

        public bool FolderExists => false;

        public Task<bool> HasReadAccessAsync() => Task.FromResult(true);

        public Task<bool> RequestReadAccessAsync() => Task.FromResult(true);
    }
}
