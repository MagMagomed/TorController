using System.Text.Json;
using System.IO;

namespace TorController.App.Storage;

public sealed class JsonCollectionStore<T> : IJsonCollectionStore<T>
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _filePath;

    public JsonCollectionStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    public async Task<IReadOnlyList<T>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        await using var stream = new FileStream(
            _filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        var items = await JsonSerializer.DeserializeAsync<List<T>>(stream, SerializerOptions, cancellationToken);
        return items ?? throw new JsonException($"JSON collection '{_filePath}' contains null instead of an array.");
    }

    public async Task SaveAsync(IReadOnlyCollection<T> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException($"Could not determine the directory for '{_filePath}'.");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(directory, $"{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, items, SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
