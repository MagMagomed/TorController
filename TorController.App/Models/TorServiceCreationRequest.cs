namespace TorController.App.Models;

public sealed record TorServiceCreationRequest(
    string ServiceName,
    string DisplayName,
    string TorExecutablePath,
    string DataDirectory,
    string StartingType,
    TorConfiguration Configuration);
