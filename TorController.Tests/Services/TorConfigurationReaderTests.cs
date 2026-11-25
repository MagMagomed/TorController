using TorController.App.Services;

namespace TorController.Tests.Services;

[TestClass]
public sealed class TorConfigurationReaderTests
{
    [TestMethod]
    public async Task ReadAsyncReadsFileAndParsesConfiguration()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "torrc");
        await File.WriteAllTextAsync(path, "SocksPort 9050\nUseBridges 1");
        var reader = new TorConfigurationReader(new TorConfigParser());

        var result = await reader.ReadAsync(path);

        CollectionAssert.AreEqual(new[] { 9050 }, result.SocksPorts);
        Assert.IsTrue(result.UseBridges);
    }

    [TestMethod]
    public async Task ReadAsyncSurfacesMissingFile()
    {
        using var directory = new TemporaryDirectory();
        var reader = new TorConfigurationReader(new TorConfigParser());

        await Assert.ThrowsExceptionAsync<FileNotFoundException>(() =>
            reader.ReadAsync(Path.Combine(directory.Path, "missing-torrc")));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
