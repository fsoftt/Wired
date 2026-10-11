using Wired.Core.Localization;
using Wired.Presentation.Services;

namespace Wired.App.Services;

/// <summary>
/// Read access to the shared music folder. On Android, files left by a previous installation belong
/// to it, so reading them needs READ_MEDIA_AUDIO (13+) or READ_EXTERNAL_STORAGE (11–12).
/// Other platforms need no permission.
/// </summary>
public sealed class MusicFolderAccess(Wired.Core.Abstractions.IAppPaths paths) : IMusicFolderAccess
{
    public bool FolderExists => Directory.Exists(paths.MusicDirectory);

    public string DisplayLocation =>
#if ANDROID
        MauiAppPaths.IsMusicDirectoryShared ? L.T("Folder_Shared") : L.T("Folder_AppStorage");
#elif WINDOWS
        L.T("Folder_Shared").Replace('/', '\\');
#else
        L.T("Folder_AppStorage");
#endif

    public async Task<bool> HasReadAccessAsync()
    {
#if ANDROID
        return await Permissions.CheckStatusAsync<ReadAudioPermission>() == PermissionStatus.Granted;
#else
        return await Task.FromResult(true);
#endif
    }

    public async Task<bool> RequestReadAccessAsync()
    {
#if ANDROID
        if (await HasReadAccessAsync())
            return true;

        var status = await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<ReadAudioPermission>);
        return status == PermissionStatus.Granted;
#else
        return await Task.FromResult(true);
#endif
    }

#if ANDROID
    private sealed class ReadAudioPermission : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
            OperatingSystem.IsAndroidVersionAtLeast(33)
                ? [(Android.Manifest.Permission.ReadMediaAudio, true)]
                : [(Android.Manifest.Permission.ReadExternalStorage, true)];
    }
#endif
}
