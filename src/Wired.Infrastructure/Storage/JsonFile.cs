using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Wired.Infrastructure.Storage;

/// <summary>
/// A list of items persisted as a JSON file, cached in memory and written atomically.
/// Thread safe: every operation is serialized.
/// </summary>
internal sealed class JsonFile<T>(string path, JsonTypeInfo<List<T>> typeInfo)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<T>? _items;

    public async Task<TResult> ReadAsync<TResult>(Func<IReadOnlyList<T>, TResult> read, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return read(await LoadAsync(cancellationToken));
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task MutateAsync(Action<List<T>> mutation, CancellationToken cancellationToken) =>
        MutateAsync(
            items =>
            {
                mutation(items);
                return (true, true);
            },
            cancellationToken);

    /// <summary>Runs <paramref name="mutation"/>, which says whether it changed anything worth saving.</summary>
    public async Task<TResult> MutateAsync<TResult>(Func<List<T>, (TResult Result, bool Changed)> mutation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = await LoadAsync(cancellationToken);
            var (result, changed) = mutation(items);
            if (changed)
                await SaveAsync(items, cancellationToken);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<T>> LoadAsync(CancellationToken cancellationToken)
    {
        if (_items is not null)
            return _items;

        if (!File.Exists(path))
            return _items = [];

        await using var stream = File.OpenRead(path);
        return _items = await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken) ?? [];
    }

    private async Task SaveAsync(List<T> items, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Write to a temp file first so a crash never leaves a corrupted file.
        var tempFile = path + ".tmp";
        await using (var stream = File.Create(tempFile))
        {
            await JsonSerializer.SerializeAsync(stream, items, typeInfo, cancellationToken);
        }

        File.Move(tempFile, path, overwrite: true);
    }
}
