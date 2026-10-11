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
                await dialogs.ShowToastAsync("Wired no reconoce ese enlace con las fuentes instaladas.");
                return;
            }

            await dialogs.ShowToastAsync($"Descargando \"{track.Title}\"");
            await navigation.OpenDownloadsAsync();
        }
        catch (Exception error)
        {
            await dialogs.ShowErrorAsync("No se pudo agregar la descarga", error.Message, error.ToString());
        }
    }
}
