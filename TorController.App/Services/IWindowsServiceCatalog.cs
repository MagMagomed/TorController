using TorController.App.Models;

namespace TorController.App.Services;

public interface IWindowsServiceCatalog
{
    Task<IReadOnlyList<WindowsServiceInfo>> FindTorServicesAsync(CancellationToken cancellationToken = default);
    Task<bool> ServiceNameExistsAsync(string serviceName, CancellationToken cancellationToken = default);
}
