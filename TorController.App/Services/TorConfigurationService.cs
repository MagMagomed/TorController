using TorController.App.Infrastructure;
using TorController.App.Models;
using System.Globalization;
using System.IO;

namespace TorController.App.Services;

public sealed class TorConfigurationService
{
    private readonly IFileSystem _fileSystem;
    private readonly TorConfigurationVerifier _verifier;
    private readonly TorConfigDocumentEditor _documentEditor;

    public TorConfigurationService(
        IFileSystem fileSystem,
        TorConfigurationVerifier verifier,
        TorConfigDocumentEditor documentEditor)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _documentEditor = documentEditor ?? throw new ArgumentNullException(nameof(documentEditor));
    }

    public async Task<ConfigurationSaveResult> SaveAsync(
        TorService service,
        TorConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(configuration);
        if (string.IsNullOrWhiteSpace(service.TorExecutablePath) ||
            string.IsNullOrWhiteSpace(service.ConfigurationFilePath))
        {
            return new ConfigurationSaveResult(false, "The Tor executable or configuration file path is missing.", null);
        }

        if (!_fileSystem.FileExists(service.ConfigurationFilePath))
        {
            return new ConfigurationSaveResult(false, "The Tor configuration file was not found.", null);
        }

        string? backupPath = null;
        string? temporaryPath = null;
        try
        {
            var currentContent = await _fileSystem.ReadAllTextAsync(service.ConfigurationFilePath, cancellationToken);
            backupPath = GetBackupPath(service.ConfigurationFilePath);
            _fileSystem.CopyFile(service.ConfigurationFilePath, backupPath, overwrite: false);

            var updatedContent = _documentEditor.Apply(currentContent, configuration);
            temporaryPath = $"{service.ConfigurationFilePath}.{Guid.NewGuid():N}.tmp";
            await _fileSystem.WriteAllTextAsync(temporaryPath, updatedContent, cancellationToken);

            var verification = await _verifier.VerifyAsync(
                service.TorExecutablePath,
                temporaryPath,
                cancellationToken);
            if (!verification.IsValid)
            {
                var cleanupMessage = DeleteTemporaryFile(temporaryPath);
                return new ConfigurationSaveResult(
                    false,
                    cleanupMessage.Length == 0 ? verification.Message : $"{verification.Message} {cleanupMessage}",
                    backupPath);
            }

            _fileSystem.MoveFile(temporaryPath, service.ConfigurationFilePath, overwrite: true);
            return new ConfigurationSaveResult(true, "Configuration saved and verified.", backupPath);
        }
        catch (Exception exception) when (IsOperationFailure(exception))
        {
            var cleanupMessage = temporaryPath is null ? string.Empty : DeleteTemporaryFile(temporaryPath);
            var message = $"Could not save Tor configuration: {exception.Message}";
            if (cleanupMessage.Length > 0)
            {
                message = $"{message} {cleanupMessage}";
            }

            return new ConfigurationSaveResult(false, message, backupPath);
        }
    }

    private string GetBackupPath(string configurationPath)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        var candidate = $"{configurationPath}.{timestamp}.bak";
        var suffix = 1;
        while (_fileSystem.FileExists(candidate))
        {
            candidate = $"{configurationPath}.{timestamp}.{suffix++}.bak";
        }

        return candidate;
    }

    private string DeleteTemporaryFile(string path)
    {
        try
        {
            _fileSystem.DeleteFile(path);
            return string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"Could not remove temporary file: {exception.Message}";
        }
    }

    private static bool IsOperationFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException or System.ComponentModel.Win32Exception;
}
