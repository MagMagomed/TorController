using TorController.App.Models;

namespace TorController.App.Services;

public interface ITorConfigurationReader
{
    Task<TorConfiguration> ReadAsync(string path, CancellationToken cancellationToken = default);
}
