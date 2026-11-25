using TorController.App.Infrastructure;
using TorController.App.Models;
using TorController.App.Services;

namespace TorController.Tests.Services;

[TestClass]
public sealed class TorServiceFileCleanerTests
{
    [TestMethod]
    public void CleanupDeletesCreatedTorrcAndDataDirectoryButPreservesExternalFiles()
    {
        var fileSystem = new FakeFileSystem();
        var service = CreateOwnedService();
        var configuration = CreateConfiguration();
        service.Configuration = configuration.Id;
        fileSystem.Files[service.ApplicationCreatedTorrcPath!] = "DataDirectory C:\\Tor\\Data";
        fileSystem.Files[@"C:\Tor\Data\cached-certs"] = "mock state";
        fileSystem.Files[@"C:\Tor\Data\inside.log"] = "mock log";
        fileSystem.Files[service.TorExecutablePath] = "mock tor";
        fileSystem.Files[@"C:\Tor\lyrebird.exe"] = "mock plugin";
        fileSystem.Files[@"C:\Tor\Logs\outside.log"] = "mock log";
        fileSystem.Directories.Add(@"C:\Tor\Data");

        var result = new TorServiceFileCleaner(fileSystem).Cleanup(
            service, [service], [configuration]);

        Assert.IsTrue(result.Success, result.Message);
        Assert.IsFalse(fileSystem.Files.ContainsKey(service.ApplicationCreatedTorrcPath!));
        Assert.IsFalse(fileSystem.Directories.Contains(@"C:\Tor\Data"));
        Assert.IsFalse(fileSystem.Files.ContainsKey(@"C:\Tor\Data\cached-certs"));
        Assert.IsTrue(fileSystem.Files.ContainsKey(service.TorExecutablePath));
        Assert.IsTrue(fileSystem.Files.ContainsKey(@"C:\Tor\lyrebird.exe"));
        Assert.IsTrue(fileSystem.Files.ContainsKey(@"C:\Tor\Logs\outside.log"));
    }

    [TestMethod]
    public void CleanupKeepsFilesForPreviouslyDiscoveredService()
    {
        var fileSystem = new FakeFileSystem();
        var service = new TorService
        {
            Id = Guid.NewGuid(),
            Name = "tor",
            ConfigurationFilePath = @"C:\Tor\torrc"
        };
        fileSystem.Files[@"C:\Tor\torrc"] = "unowned configuration";
        fileSystem.Directories.Add(@"C:\Tor\Data");

        var result = new TorServiceFileCleaner(fileSystem).Cleanup(service, [service], []);

        Assert.IsTrue(result.Success, result.Message);
        Assert.IsTrue(result.Message.Contains("not created", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(fileSystem.Files.ContainsKey(@"C:\Tor\torrc"));
        Assert.IsTrue(fileSystem.Directories.Contains(@"C:\Tor\Data"));
    }

    [TestMethod]
    public void CleanupRejectsDataDirectoryReferencedByAnotherService()
    {
        var fileSystem = new FakeFileSystem();
        var service = CreateOwnedService();
        var otherService = new TorService
        {
            Id = Guid.NewGuid(),
            Name = "tor-other",
            ConfigurationFilePath = @"C:\Tor\Data\other-torrc"
        };
        var configuration = CreateConfiguration();
        service.Configuration = configuration.Id;
        fileSystem.Files[service.ApplicationCreatedTorrcPath!] = "torrc";
        fileSystem.Files[otherService.ConfigurationFilePath] = "other config";
        fileSystem.Directories.Add(@"C:\Tor\Data");

        var result = new TorServiceFileCleaner(fileSystem).Cleanup(
            service, [service, otherService], [configuration]);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Message.Contains("referenced", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(fileSystem.Files.ContainsKey(service.ApplicationCreatedTorrcPath!));
        Assert.IsTrue(fileSystem.Files.ContainsKey(otherService.ConfigurationFilePath));
        Assert.IsTrue(fileSystem.Directories.Contains(@"C:\Tor\Data"));
    }

    [TestMethod]
    public void CleanupRejectsRootAndReparsePointPathsWithoutDeletingAnything()
    {
        var fileSystem = new FakeFileSystem();
        var root = Path.GetPathRoot(Environment.CurrentDirectory)!;
        var service = CreateOwnedService();
        service.ApplicationCreatedTorrcPath = Path.Combine(root, "torrc");
        service.ApplicationCreatedDataDirectory = root;
        var configuration = CreateConfiguration();
        service.Configuration = configuration.Id;
        fileSystem.Files[service.ApplicationCreatedTorrcPath!] = "keep";

        var rootResult = new TorServiceFileCleaner(fileSystem).Cleanup(
            service, [service], [configuration]);

        Assert.IsFalse(rootResult.Success);
        Assert.IsTrue(rootResult.Message.Contains("root", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(fileSystem.Files.ContainsKey(service.ApplicationCreatedTorrcPath!));

        service = CreateOwnedService();
        fileSystem.Files[service.ApplicationCreatedTorrcPath!] = "keep";
        fileSystem.Directories.Add(@"C:\Tor\Data");
        fileSystem.ReparsePoints.Add(@"C:\Tor\Data\junction");
        configuration = CreateConfiguration();
        service.Configuration = configuration.Id;
        var reparseResult = new TorServiceFileCleaner(fileSystem).Cleanup(
            service, [service], [configuration]);

        Assert.IsFalse(reparseResult.Success);
        Assert.IsTrue(reparseResult.Message.Contains("link", StringComparison.OrdinalIgnoreCase) ||
                      reparseResult.Message.Contains("reparse", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(fileSystem.Files.ContainsKey(service.ApplicationCreatedTorrcPath!));
    }

    [TestMethod]
    public void CleanupRejectsIncompleteOwnershipMetadata()
    {
        var fileSystem = new FakeFileSystem();
        var service = CreateOwnedService();
        service.ApplicationCreatedDataDirectory = null;
        var configuration = CreateConfiguration();
        service.Configuration = configuration.Id;

        var result = new TorServiceFileCleaner(fileSystem).Cleanup(
            service, [service], [configuration]);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Message.Contains("ownership", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void CleanupRefusesOwnedFilesWhileWindowsServiceIsRegistered()
    {
        var fileSystem = new FakeFileSystem();
        var service = CreateOwnedService();
        service.WindowsServiceRemoved = false;
        var configuration = CreateConfiguration();
        service.Configuration = configuration.Id;
        fileSystem.Files[service.ApplicationCreatedTorrcPath!] = "torrc";

        var result = new TorServiceFileCleaner(fileSystem).Cleanup(
            service, [service], [configuration]);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Message.Contains("still registered", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(fileSystem.Files.ContainsKey(service.ApplicationCreatedTorrcPath!));
    }

    [TestMethod]
    public void CleanupRejectsPluginInsideOwnedDataDirectory()
    {
        var fileSystem = new FakeFileSystem();
        var service = CreateOwnedService();
        var configuration = CreateConfiguration();
        service.Configuration = configuration.Id;
        configuration.ClientTransportPlugins = [@"obfs4 exec C:\Tor\Data\lyrebird.exe"];
        fileSystem.Files[service.ApplicationCreatedTorrcPath!] = "torrc";

        var result = new TorServiceFileCleaner(fileSystem).Cleanup(
            service, [service], [configuration]);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Message.Contains("transport plugin", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(fileSystem.Files.ContainsKey(service.ApplicationCreatedTorrcPath!));
    }

    [TestMethod]
    public void CleanupRejectsLogReferencedByAnotherConfiguration()
    {
        var fileSystem = new FakeFileSystem();
        var service = CreateOwnedService();
        var configuration = CreateConfiguration();
        service.Configuration = configuration.Id;
        var otherConfiguration = new TorConfiguration
        {
            Id = Guid.NewGuid(),
            Logs = [@"notice file C:\Tor\Data\shared.log"]
        };
        fileSystem.Files[service.ApplicationCreatedTorrcPath!] = "torrc";

        var result = new TorServiceFileCleaner(fileSystem).Cleanup(
            service, [service], [configuration, otherConfiguration]);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Message.Contains("another saved configuration", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(fileSystem.Files.ContainsKey(service.ApplicationCreatedTorrcPath!));
    }

    [TestMethod]
    public void CleanupTreatsFilesystemRootAsOverlappingSavedPath()
    {
        var fileSystem = new FakeFileSystem();
        var service = CreateOwnedService();
        var configuration = CreateConfiguration();
        service.Configuration = configuration.Id;
        var otherConfiguration = new TorConfiguration
        {
            Id = Guid.NewGuid(),
            DataDirectory = Path.GetPathRoot(@"C:\Tor\Data")
        };
        fileSystem.Files[service.ApplicationCreatedTorrcPath!] = "torrc";

        var result = new TorServiceFileCleaner(fileSystem).Cleanup(
            service, [service], [configuration, otherConfiguration]);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Message.Contains("another saved configuration", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(fileSystem.Files.ContainsKey(service.ApplicationCreatedTorrcPath!));
    }

    [TestMethod]
    public void CleanupReportsFileDeletionFailureAndKeepsDirectory()
    {
        var fileSystem = new FakeFileSystem
        {
            FileDeleteFailure = new IOException("file is locked")
        };
        var service = CreateOwnedService();
        var configuration = CreateConfiguration();
        service.Configuration = configuration.Id;
        fileSystem.Files[service.ApplicationCreatedTorrcPath!] = "torrc";
        fileSystem.Directories.Add(@"C:\Tor\Data");

        var result = new TorServiceFileCleaner(fileSystem).Cleanup(
            service, [service], [configuration]);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Message.Contains("file is locked", StringComparison.Ordinal));
        Assert.IsTrue(fileSystem.Files.ContainsKey(service.ApplicationCreatedTorrcPath!));
        Assert.IsTrue(fileSystem.Directories.Contains(@"C:\Tor\Data"));
    }

    [TestMethod]
    public void ConstructorAndCleanupRejectNullArguments()
    {
        Assert.ThrowsException<ArgumentNullException>(() => new TorServiceFileCleaner(null!));
        var cleaner = new TorServiceFileCleaner(new FakeFileSystem());
        var service = CreateOwnedService();

        Assert.ThrowsException<ArgumentNullException>(() => cleaner.Cleanup(null!, [service], []));
        Assert.ThrowsException<ArgumentNullException>(() => cleaner.Cleanup(service, null!, []));
        Assert.ThrowsException<ArgumentNullException>(() => cleaner.Cleanup(service, [service], null!));
    }

    private static TorService CreateOwnedService() =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "tor",
            TorExecutablePath = @"C:\Tor\tor.exe",
            ConfigurationFilePath = @"C:\Tor\Data\torrc",
            ApplicationCreatedTorrcPath = @"C:\Tor\Data\torrc",
            ApplicationCreatedDataDirectory = @"C:\Tor\Data",
            WindowsServiceRemoved = true,
            Configuration = Guid.NewGuid()
        };

    private static TorConfiguration CreateConfiguration() =>
        new()
        {
            Id = Guid.NewGuid(),
            DataDirectory = @"C:\Tor\Data",
            ClientTransportPlugins = [@"obfs4 exec C:\Tor\lyrebird.exe"],
            Logs = [@"notice file C:\Tor\Data\inside.log", @"notice file C:\Tor\Logs\outside.log"]
        };

    private sealed class FakeFileSystem : IFileSystem
    {
        public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ReparsePoints { get; } = new(StringComparer.OrdinalIgnoreCase);
        public IOException? FileDeleteFailure { get; init; }

        public bool FileExists(string path) => Files.ContainsKey(path);
        public bool DirectoryExists(string path) => Directories.Contains(path);
        public void CreateDirectory(string path) => Directories.Add(path);
        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(Files[path]);
        public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
        {
            Files[path] = content;
            return Task.CompletedTask;
        }
        public void CopyFile(string sourcePath, string destinationPath, bool overwrite) =>
            Files[destinationPath] = Files[sourcePath];
        public void MoveFile(string sourcePath, string destinationPath, bool overwrite)
        {
            Files[destinationPath] = Files[sourcePath];
            Files.Remove(sourcePath);
        }
        public void DeleteFile(string path)
        {
            if (FileDeleteFailure is not null)
            {
                throw FileDeleteFailure;
            }

            Files.Remove(path);
        }
        public void DeleteDirectory(string path)
        {
            foreach (var file in Files.Keys.Where(file => IsAtOrBelow(file, path)).ToArray())
            {
                Files.Remove(file);
            }

            foreach (var directory in Directories.Where(directory => IsAtOrBelow(directory, path)).ToArray())
            {
                Directories.Remove(directory);
            }
        }
        public bool HasReparsePoints(string path) =>
            ReparsePoints.Any(reparsePoint => IsAtOrBelow(reparsePoint, path));

        private static bool IsAtOrBelow(string path, string parent)
        {
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return fullPath.Equals(fullParent, StringComparison.OrdinalIgnoreCase) ||
                   fullPath.StartsWith(fullParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }
}
