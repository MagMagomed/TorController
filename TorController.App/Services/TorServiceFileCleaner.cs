using System.IO;
using System.Text.RegularExpressions;
using TorController.App.Infrastructure;
using TorController.App.Models;

namespace TorController.App.Services;

public sealed class TorServiceFileCleaner
{
    private static readonly Regex PluginExecutablePattern = new(
        @"(?:^|\s)exec\s+(?:""([^""]+)""|([^\s""]+))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LogFilePattern = new(
        @"^\s*.*?\bfile\s+(.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IFileSystem _fileSystem;

    public sealed record CleanupResult(bool Success, string Message);

    public TorServiceFileCleaner(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public CleanupResult Cleanup(
        TorService service,
        IReadOnlyCollection<TorService> services,
        IReadOnlyCollection<TorConfiguration> configurations)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configurations);

        var torrcPath = service.ApplicationCreatedTorrcPath;
        var dataDirectory = service.ApplicationCreatedDataDirectory;
        if (string.IsNullOrWhiteSpace(torrcPath) && string.IsNullOrWhiteSpace(dataDirectory))
        {
            return new CleanupResult(true, "Files not created by this application were kept.");
        }

        if (string.IsNullOrWhiteSpace(torrcPath) || string.IsNullOrWhiteSpace(dataDirectory))
        {
            return Failure("The saved ownership metadata is incomplete; the Tor files were kept.", torrcPath, dataDirectory);
        }

        if (!service.WindowsServiceRemoved)
        {
            return Failure("The Windows service is still registered; the Tor files were kept.", torrcPath, dataDirectory);
        }

        string fullTorrcPath;
        string fullDataDirectory;
        try
        {
            if (!Path.IsPathFullyQualified(torrcPath) || !Path.IsPathFullyQualified(dataDirectory))
            {
                return Failure("The saved ownership paths are not absolute; the Tor files were kept.", torrcPath, dataDirectory);
            }

            fullTorrcPath = Path.GetFullPath(torrcPath);
            fullDataDirectory = NormalizeDirectory(dataDirectory);
            var root = Path.GetPathRoot(fullDataDirectory);
            if (root is null || PathsEqual(root, fullDataDirectory))
            {
                return Failure("The saved data directory is a filesystem root; the Tor files were kept.", torrcPath, dataDirectory);
            }

            var torrcParent = Path.GetDirectoryName(fullTorrcPath);
            if (!string.Equals(Path.GetFileName(fullTorrcPath), "torrc", StringComparison.OrdinalIgnoreCase) ||
                torrcParent is null ||
                !PathsEqual(NormalizeDirectory(torrcParent), fullDataDirectory))
            {
                return Failure("The saved torrc is not directly inside the owned data directory; the Tor files were kept.", torrcPath, dataDirectory);
            }
        }
        catch (Exception exception) when (IsPathException(exception))
        {
            return Failure($"The saved ownership paths are invalid; the Tor files were kept. {exception.Message}", torrcPath, dataDirectory);
        }

        var configuration = configurations.FirstOrDefault(item => item.Id == service.Configuration);
        if (configuration is null)
        {
            return Failure("The service configuration is unavailable; the Tor files were kept.", torrcPath, dataDirectory);
        }

        string? referenceError;
        try
        {
            referenceError = ValidateReferences(
                service,
                services,
                configurations,
                configuration,
                fullTorrcPath,
                fullDataDirectory);
        }
        catch (Exception exception) when (IsPathException(exception))
        {
            return Failure($"A saved reference path is invalid; the Tor files were kept. {exception.Message}", torrcPath, dataDirectory);
        }

        if (referenceError is not null)
        {
            return Failure(referenceError, torrcPath, dataDirectory);
        }

        try
        {
            if (_fileSystem.HasReparsePoints(fullDataDirectory) ||
                _fileSystem.HasReparsePoints(fullTorrcPath))
            {
                return Failure("The owned path contains a symbolic link or reparse point; the Tor files were kept.", torrcPath, dataDirectory);
            }

            if (_fileSystem.DirectoryExists(fullTorrcPath) ||
                (_fileSystem.FileExists(fullTorrcPath) && !_fileSystem.DirectoryExists(fullDataDirectory)))
            {
                return Failure("The saved torrc or data-directory path has an unexpected file type; the Tor files were kept.", torrcPath, dataDirectory);
            }

            _fileSystem.DeleteFile(fullTorrcPath);
            if (_fileSystem.DirectoryExists(fullDataDirectory))
            {
                _fileSystem.DeleteDirectory(fullDataDirectory);
            }

            return new CleanupResult(true, "The application-created torrc and data directory were deleted.");
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return Failure(
                $"Could not fully remove the application-created Tor files; remaining files were kept for retry. {exception.Message}",
                torrcPath,
                dataDirectory);
        }
    }

    private static string? ValidateReferences(
        TorService service,
        IReadOnlyCollection<TorService> services,
        IReadOnlyCollection<TorConfiguration> configurations,
        TorConfiguration ownConfiguration,
        string torrcPath,
        string dataDirectory)
    {
        if (!PathsEqual(NormalizeDirectory(ownConfiguration.DataDirectory ?? string.Empty), dataDirectory))
        {
            return "The current configuration does not match the owned data directory; the Tor files were kept.";
        }

        foreach (var otherService in services.Where(item => item.Id != service.Id))
        {
            var paths = new[]
            {
                otherService.TorExecutablePath,
                otherService.ConfigurationFilePath,
                otherService.ApplicationCreatedTorrcPath,
                otherService.ApplicationCreatedDataDirectory
            };
            foreach (var path in paths)
            {
                if (PathOverlapsDirectory(path, dataDirectory))
                {
                    return $"The owned data directory is referenced by service '{otherService.Name}'; the Tor files were kept.";
                }
            }
        }

        if (PathOverlapsDirectory(service.TorExecutablePath, dataDirectory))
        {
            return "The Tor executable is inside or overlaps the owned data directory; the Tor files were kept.";
        }

        if (!PathsEqual(service.ConfigurationFilePath, torrcPath) &&
            PathOverlapsDirectory(service.ConfigurationFilePath, dataDirectory))
        {
            return "The service uses another configuration path inside the owned data directory; the Tor files were kept.";
        }

        foreach (var path in GetPluginExecutablePaths(ownConfiguration))
        {
            if (PathOverlapsDirectory(path, dataDirectory))
            {
                return "A transport plugin is inside or overlaps the owned data directory; the Tor files were kept.";
            }
        }

        foreach (var otherConfiguration in configurations.Where(item => item.Id != ownConfiguration.Id))
        {
            if (PathOverlapsDirectory(otherConfiguration.DataDirectory, dataDirectory))
            {
                return "The owned data directory is referenced by another saved configuration; the Tor files were kept.";
            }

            var referencedPaths = GetPluginExecutablePaths(otherConfiguration)
                .Concat(GetLogFilePaths(otherConfiguration));
            if (referencedPaths.Any(path => PathOverlapsDirectory(path, dataDirectory)))
            {
                return "The owned data directory contains a path referenced by another saved configuration; the Tor files were kept.";
            }
        }

        foreach (var path in GetLogFilePaths(ownConfiguration))
        {
            if (PathOverlapsDirectory(path, dataDirectory) && !IsAtOrBelow(path, dataDirectory))
            {
                return "A log path overlaps the owned data directory; the Tor files were kept.";
            }
        }

        return null;
    }

    private static IEnumerable<string> GetPluginExecutablePaths(TorConfiguration configuration)
    {
        foreach (var plugin in configuration.ClientTransportPlugins ?? [])
        {
            if (string.IsNullOrWhiteSpace(plugin))
            {
                continue;
            }

            var match = PluginExecutablePattern.Match(plugin);
            if (match.Success)
            {
                yield return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            }
        }
    }

    private static IEnumerable<string> GetLogFilePaths(TorConfiguration configuration)
    {
        foreach (var log in configuration.Logs ?? [])
        {
            if (string.IsNullOrWhiteSpace(log))
            {
                continue;
            }

            var match = LogFilePattern.Match(log);
            if (match.Success)
            {
                yield return match.Groups[1].Value.Trim().Trim('"');
            }
        }
    }

    private static bool PathOverlapsDirectory(string? path, string directory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            if (!Path.IsPathFullyQualified(path))
            {
                return true;
            }

            var normalizedPath = Path.GetFullPath(path);
            return IsAtOrBelow(normalizedPath, directory) || IsAtOrBelow(directory, normalizedPath);
        }
        catch (Exception exception) when (IsPathException(exception))
        {
            return true;
        }
    }

    private static bool IsAtOrBelow(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path);
        var fullDirectory = NormalizeDirectory(directory);
        var directoryPrefix = Path.EndsInDirectorySeparator(fullDirectory)
            ? fullDirectory
            : fullDirectory + Path.DirectorySeparatorChar;
        return PathsEqual(fullPath, fullDirectory) ||
               fullPath.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        return root is not null && PathsEqual(fullPath, root)
            ? root
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            StringComparison.OrdinalIgnoreCase);

    private static CleanupResult Failure(string message, string? torrcPath = null, string? dataDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(torrcPath) && string.IsNullOrWhiteSpace(dataDirectory))
        {
            return new CleanupResult(false, message);
        }

        return new CleanupResult(
            false,
            $"{message} torrc: {torrcPath ?? "unknown"}; data directory: {dataDirectory ?? "unknown"}.");
    }

    private static bool IsPathException(Exception exception) =>
        exception is ArgumentException or IOException or NotSupportedException;

    private static bool IsRecoverable(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or
            ArgumentException or NotSupportedException;
}
