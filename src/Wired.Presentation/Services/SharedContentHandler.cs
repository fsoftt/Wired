using Wired.Core.Localization;
using Wired.Core.UseCases;

namespace Wired.Presentation.Services;

/// <summary>Handles content shared to Wired from other apps (e.g. a link to a song).</summary>
public sealed class SharedContentHandler(
    DownloadSharedLinkUseCase downloadSharedLink,
    IDialogService dialogs,
    INavigationService navigation)
{
    public async Task HandleTextAsync(string sharedText)
    {
        try
        {
            var track = await downloadSharedLink.ExecuteAsync(sharedText);
            if (track is null)
            {
                await dialogs.ShowToastAsync(L.T("Share_Unrecognized"));
                return;
            }

            await dialogs.ShowToastAsync(L.T("Download_Starting", track.Title));
            await navigation.OpenDownloadsAsync();
        }
        catch (Exception error)
        {
            await dialogs.ShowErrorAsync(L.T("Share_AddFailed"), error.Message, error.ToString());
        }
    }
}
