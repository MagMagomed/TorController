namespace TorController.App.Models;

public sealed record TorServiceCreationResult(
    bool Success,
    bool ServiceRegistered,
    string Message,
    string? ConfigurationPath,
    string? DataDirectory);
