using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Wired.Presentation.Services;

namespace Wired.App;

/// <summary>
/// Besides launching the app, receives text shared from other apps (a "share" to Wired from a music app)
/// and downloads the song.
/// </summary>
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter([Intent.ActionSend], Categories = [Intent.CategoryDefault], DataMimeType = "text/plain", Label = "@string/share_with_wired")]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        HandleShare(Intent);
    }

    /// <summary>Wired already open (single top): the share arrives here.</summary>
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleShare(intent);
    }

    private static void HandleShare(Intent? intent)
    {
        if (intent?.Action != Intent.ActionSend || intent.GetStringExtra(Intent.ExtraText) is not { Length: > 0 } text)
            return;

        // Forget the share once handled so rotating the screen does not download it again.
        intent.RemoveExtra(Intent.ExtraText);

        var handler = IPlatformApplication.Current?.Services.GetService<SharedContentHandler>();
        if (handler is not null)
            _ = HandleWhenReadyAsync(handler, text);
    }

    /// <summary>On a cold start the shell is still being built; wait for it before navigating.</summary>
    private static async Task HandleWhenReadyAsync(SharedContentHandler handler, string text)
    {
        for (var attempt = 0; attempt < 50 && Shell.Current is null; attempt++)
            await Task.Delay(100);

        await MainThread.InvokeOnMainThreadAsync(() => handler.HandleTextAsync(text));
    }
}
