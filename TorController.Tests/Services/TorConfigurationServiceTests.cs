using TorController.App.Infrastructure;
using TorController.App.Models;
using TorController.App.Services;

namespace TorController.Tests.Services;

[TestClass]
public sealed class TorConfigurationServiceTests
{
    [TestMethod]
    public async Task SaveAsyncCreatesBackupVerifiesAndReplacesConfiguration()
    {
        var fileSystem = new FakeFileSystem();
        fileSystem.Files[@"C:\Tor\torrc"] = "SocksPort 9050";
        string? verifiedPath = null;
        var verifier = new TorConfigurationVerifier((_, path, _) =>
        {
            verifiedPath = path;
            return Task.FromResult(0);
        });
        var service = CreateService(fileSystem, verifier);

        var result = await service.SaveAsync(
            CreateTorService(),
            new TorConfiguration { SocksPorts = [9052] });

        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.BackupPath);
        Assert.AreEqual("SocksPort 9050", fileSystem.Files[result.BackupPath]);
        Assert.IsTrue(fileSystem.Files[@"C:\Tor\torrc"].Contains("SocksPort 9052", StringComparison.Ordinal));
        Assert.IsTrue(verifiedPath!.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SaveAsyncKeepsOriginalWhenTorRejectsCandidate()
    {
        var fileSystem = new FakeFileSystem();
        fileSystem.Files[@"C:\Tor\torrc"] = "SocksPort 9050";
        var verifier = new TorConfigurationVerifier((_, _, _) => Task.FromResult(1));

        var result = await CreateService(fileSystem, verifier).SaveAsync(
            CreateTorService(),
            new TorConfiguration { SocksPorts = [9052] });

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.BackupPath);
        Assert.AreEqual("SocksPort 9050", fileSystem.Files[@"C:\Tor\torrc"]);
        Assert.IsFalse(fileSystem.Files.Keys.Any(path => path.EndsWith(".tmp", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task SaveAsyncReportsMissingConfigWithoutClaimingSuccess()
    {
        var fileSystem = new FakeFileSystem();

        var result = await CreateService(
            fileSystem,
            new TorConfigurationVerifier((_, _, _) => Task.FromResult(0)))
            .SaveAsync(CreateTorService(), new TorConfiguration());

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Message.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task SaveAsyncReportsFileErrors()
    {
        var fileSystem = new FakeFileSystem { ThrowOnCopy = true };
        fileSystem.Files[@"C:\Tor\torrc"] = "SocksPort 9050";

        var result = await CreateService(
            fileSystem,
            new TorConfigurationVerifier((_, _, _) => Task.FromResult(0)))
            .SaveAsync(CreateTorService(), new TorConfiguration());

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Message.Contains("copy", StringComparison.OrdinalIgnoreCase));
    }

    private static TorConfigurationService CreateService(FakeFileSystem fileSystem, TorConfigurationVerifier verifier) =>
        new(fileSystem, verifier, new TorConfigDocumentEditor());

    private static TorService CreateTorService() => new()
    {
        Name = "tor",
        TorExecutablePath = @"C:\Tor\tor.exe",
        ConfigurationFilePath = @"C:\Tor\torrc"
    };

    private sealed class FakeFileSystem : IFileSystem
    {
        public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool ThrowOnCopy { get; init; }

        public bool FileExists(string path) => Files.ContainsKey(path);
        public bool DirectoryExists(string path) => false;
        public void CreateDirectory(string path) { }

        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(Files[path]);

        public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
        {
            Files[path] = content;
            return Task.CompletedTask;
        }

        public void CopyFile(string sourcePath, string destinationPath, bool overwrite)
        {
            if (ThrowOnCopy)
            {
                throw new IOException("copy failed");
            }

            if (!overwrite && Files.ContainsKey(destinationPath))
            {
                throw new IOException("destination exists");
            }

            Files[destinationPath] = Files[sourcePath];
        }

        public void MoveFile(string sourcePath, string destinationPath, bool overwrite)
        {
            if (!overwrite && Files.ContainsKey(destinationPath))
            {
                throw new IOException("destination exists");
            }

            Files[destinationPath] = Files[sourcePath];
            Files.Remove(sourcePath);
        }

        public void DeleteFile(string path) => Files.Remove(path);
        public void DeleteDirectory(string path) { }
        public bool HasReparsePoints(string path) => false;
    }
}
