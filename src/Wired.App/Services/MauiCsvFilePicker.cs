using Wired.Core.Localization;
using Wired.Presentation.Services;

namespace Wired.App.Services;

public sealed class MauiCsvFilePicker : ICsvFilePicker
{
    /// <summary>File managers label CSV files in many ways; accept all of them.</summary>
    private static readonly FilePickerFileType CsvFiles = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.Android] = ["text/csv", "text/comma-separated-values", "application/csv", "application/vnd.ms-excel", "text/plain"],
        [DevicePlatform.iOS] = ["public.comma-separated-values-text", "public.plain-text"],
        [DevicePlatform.MacCatalyst] = ["public.comma-separated-values-text", "public.plain-text"],
        [DevicePlatform.WinUI] = [".csv"],
    });

    public async Task<PickedFile?> PickCsvAsync()
    {
        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = L.T("Import_PickerTitle"),
            FileTypes = CsvFiles,
        });

        return result is null ? null : new PickedFile(result.FileName, await result.OpenReadAsync());
    }
}
