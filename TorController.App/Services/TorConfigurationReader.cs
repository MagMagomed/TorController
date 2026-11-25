using TorController.App.Models;
using System.IO;

namespace TorController.App.Services;

public sealed class TorConfigurationReader : ITorConfigurationReader
{
    private readonly TorConfigParser _parser;

    public TorConfigurationReader(TorConfigParser parser)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    public async Task<TorConfiguration> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var content = await File.ReadAllTextAsync(path, cancellationToken);
        return _parser.Parse(content);
    }
}
