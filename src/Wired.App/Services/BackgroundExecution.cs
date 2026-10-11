using Wired.Presentation.Services;

namespace Wired.App.Services;

/// <summary>
/// Android battery optimizations can still cut network for long downloads with the screen off;
/// the user can exempt Wired. Other platforms have no such restriction.
/// </summary>
public sealed class BackgroundExecution : IBackgroundExecution
{
    public bool IsRestricted
    {
        get
        {
#if ANDROID
            var context = Platform.AppContext;
            var power = (Android.OS.PowerManager?)context.GetSystemService(Android.Content.Context.PowerService);
            return power is not null && !power.IsIgnoringBatteryOptimizations(context.PackageName);
#else
            return false;
#endif
        }
    }

    public Task RequestUnrestrictedAsync()
    {
#if ANDROID
        var context = Platform.AppContext;
        var intent = new Android.Content.Intent(
            Android.Provider.Settings.ActionRequestIgnoreBatteryOptimizations,
            Android.Net.Uri.Parse($"package:{context.PackageName}"));
        intent.AddFlags(Android.Content.ActivityFlags.NewTask);
        context.StartActivity(intent);
#endif
        return Task.CompletedTask;
    }
}
