namespace Wired.Presentation.Services;

/// <summary>A file chosen by the user. The caller disposes <see cref="Content"/>.</summary>
public sealed record PickedFile(string FileName, Stream Content);

public interface ICsvFilePicker
{
    /// <returns>The chosen CSV file, or null when the user canceled.</returns>
    Task<PickedFile?> PickCsvAsync();
}
