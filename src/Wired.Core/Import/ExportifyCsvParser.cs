using System.Globalization;

namespace Wired.Core.Import;

/// <summary>
/// Reads playlists exported by Exportify (https://exportify.net) from Spotify.
/// Columns are found by header name, so extra or reordered columns are fine.
/// </summary>
public static class ExportifyCsvParser
{
    private static readonly string[] TitleHeaders = ["Track Name"];
    private static readonly string[] ArtistHeaders = ["Artist Name(s)", "Artist Name"];
    private static readonly string[] AlbumHeaders = ["Album Name"];
    private static readonly string[] DurationHeaders = ["Duration (ms)", "Track Duration (ms)"];

    /// <exception cref="FormatException">The file is not an Exportify CSV.</exception>
    public static ImportedPlaylist Parse(TextReader reader, string playlistName)
    {
        using var rows = CsvReader.ReadRows(reader).GetEnumerator();
        if (!rows.MoveNext())
            throw new FormatException("El archivo está vacío.");

        var header = rows.Current.Select(h => h.Trim().TrimStart('﻿')).ToList();
        var title = Find(header, TitleHeaders) ?? throw new FormatException("No parece un CSV de Exportify: falta la columna \"Track Name\".");
        var artists = Find(header, ArtistHeaders) ?? throw new FormatException("No parece un CSV de Exportify: falta la columna \"Artist Name(s)\".");
        var album = Find(header, AlbumHeaders);
        var duration = Find(header, DurationHeaders);

        var tracks = new List<ImportedTrack>();
        while (rows.MoveNext())
        {
            var row = rows.Current;
            var name = Cell(row, title);
            if (string.IsNullOrWhiteSpace(name))
                continue; // Local files or podcasts exported without a title.

            tracks.Add(new ImportedTrack(
                name.Trim(),
                SplitArtists(Cell(row, artists)),
                album is { } a ? NullIfEmpty(Cell(row, a)) : null,
                duration is { } d && long.TryParse(Cell(row, d), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms) && ms > 0
                    ? TimeSpan.FromMilliseconds(ms)
                    : null));
        }

        return new ImportedPlaylist(playlistName, tracks);
    }

    /// <summary>Newer exports separate artists with ";", older ones with ",".</summary>
    private static List<string> SplitArtists(string value) =>
        value.Split(value.Contains(';') ? ';' : ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static int? Find(List<string> header, string[] names)
    {
        foreach (var name in names)
        {
            var index = header.FindIndex(h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                return index;
        }

        return null;
    }

    private static string Cell(IReadOnlyList<string> row, int index) => index < row.Count ? row[index] : string.Empty;

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
