using Wired.Core.Abstractions;

namespace Wired.App.Services;

/// <summary>
/// Music goes to a shared folder where the platform allows it, so it survives uninstalling the app:
/// Android 11+ → Music/Wired, Windows → My Music\Wired. Elsewhere it stays in app storage.
/// </summary>
public sealed class MauiAppPaths : IAppPaths
{
    public string MusicDirectory { get; } = ResolveMusicDirectory();

    public string DataDirectory { get; } = FileSystem.AppDataDirectory;

    public string CacheDirectory { get; } = FileSystem.CacheDirectory;

    /// <summary>Whether <see cref="MusicDirectory"/> is kept when the app is uninstalled.</summary>
    public static bool IsMusicDirectoryShared =>
#if ANDROID
        OperatingSystem.IsAndroidVersionAtLeast(30);
#elif WINDOWS
        true;
#else
        false;
#endif

    private static string ResolveMusicDirectory()
    {
#if ANDROID
        if (IsMusicDirectoryShared)
        {
            // Android 11+ lets apps create their own media files in shared folders through file paths.
#pragma warning disable CA1422 // Still the way to get the path for file APIs; MediaStore indexes the files.
            var music = Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryMusic)!.AbsolutePath;
#pragma warning restore CA1422
            return Path.Combine(music, "Wired");
        }
#elif WINDOWS
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Wired");
#endif
        return Path.Combine(FileSystem.AppDataDirectory, "music");
    }
}
