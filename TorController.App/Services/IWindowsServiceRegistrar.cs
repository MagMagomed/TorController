namespace TorController.App.Services;

public interface IWindowsServiceRegistrar
{
    Task RegisterAsync(
        string serviceName,
        string displayName,
        string torExecutablePath,
        string configurationPath,
        string startingType,
        CancellationToken cancellationToken = default);
}
