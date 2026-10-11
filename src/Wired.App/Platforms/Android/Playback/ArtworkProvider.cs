using Android.Content;
using Android.Database;
using Android.OS;
using AndroidUri = Android.Net.Uri;

namespace Wired.App.Playback;

/// <summary>
/// Serves the covers saved on the device to Android Auto (which runs in another process and cannot
/// read our files). Read-only and limited to the artwork folder.
/// </summary>
[ContentProvider([AuthoritySuffix], Name = "com.wired.player.ArtworkProvider", Exported = true)]
public sealed class ArtworkProvider : ContentProvider
{
    private const string AuthoritySuffix = "com.wired.player.artwork";

    /// <summary>content:// URI for a local cover, the URL itself for remote ones, null otherwise.</summary>
    public static AndroidUri? UriFor(Context context, string? artwork)
    {
        if (string.IsNullOrEmpty(artwork))
            return null;

        if (!File.Exists(artwork))
            return Uri.TryCreate(artwork, UriKind.Absolute, out var remote) && remote.Scheme is "http" or "https"
                ? AndroidUri.Parse(artwork)
                : null;

        return AndroidUri.Parse($"content://{AuthoritySuffix}/{AndroidUri.Encode(Path.GetFileName(artwork))}");
    }

    public override bool OnCreate() => true;

    public override ParcelFileDescriptor? OpenFile(AndroidUri uri, string mode)
    {
        var name = Path.GetFileName(uri.LastPathSegment ?? string.Empty);
        var path = Path.Combine(FileSystem.AppDataDirectory, "artwork", name);

        // Only plain file names inside the artwork folder, read-only.
        if (name.Length == 0 || !File.Exists(path))
            throw new Java.IO.FileNotFoundException(uri.ToString());

        return ParcelFileDescriptor.Open(new Java.IO.File(path), ParcelFileMode.ReadOnly);
    }

    public override string? GetType(AndroidUri uri) => "image/jpeg";

    public override ICursor? Query(AndroidUri uri, string[]? projection, string? selection, string[]? selectionArgs, string? sortOrder) => null;

    public override AndroidUri? Insert(AndroidUri uri, ContentValues? values) => null;

    public override int Delete(AndroidUri uri, string? selection, string[]? selectionArgs) => 0;

    public override int Update(AndroidUri uri, ContentValues? values, string? selection, string[]? selectionArgs) => 0;
}
