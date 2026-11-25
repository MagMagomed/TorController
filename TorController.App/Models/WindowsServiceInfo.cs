namespace TorController.App.Models;

public sealed record WindowsServiceInfo(
    string Name,
    string DisplayName,
    string State,
    string StartingType,
    string ServiceAccount,
    string CommandLine);
