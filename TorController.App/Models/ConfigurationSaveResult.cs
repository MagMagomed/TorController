namespace TorController.App.Models;

public sealed record ConfigurationSaveResult(bool Success, string Message, string? BackupPath);
