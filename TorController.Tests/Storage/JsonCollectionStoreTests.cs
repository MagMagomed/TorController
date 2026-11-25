using TorController.App.Models;
using TorController.App.Storage;

namespace TorController.Tests.Storage;

[TestClass]
public sealed class JsonCollectionStoreTests
{
    [TestMethod]
    public async Task LoadAsyncReturnsEmptyCollectionWhenFileDoesNotExist()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonCollectionStore<TorService>(Path.Combine(directory.Path, "services.json"));

        var result = await store.LoadAsync();

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task SaveAsyncAndLoadAsyncRoundTripItems()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonCollectionStore<TorService>(Path.Combine(directory.Path, "services.json"));
        var service = new TorService
        {
            Id = Guid.NewGuid(),
            Name = "tor",
            DisplayName = "Tor Win32 Service",
            TorExecutablePath = @"C:\Tor\tor.exe"
        };

        await store.SaveAsync([service]);
        var result = await store.LoadAsync();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(service.Id, result[0].Id);
        Assert.AreEqual(service.Name, result[0].Name);
        Assert.AreEqual(service.TorExecutablePath, result[0].TorExecutablePath);
    }

    [TestMethod]
    public async Task LoadAsyncReportsInvalidJson()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "services.json");
        await File.WriteAllTextAsync(path, "{ invalid");
        var store = new JsonCollectionStore<TorService>(path);

        await Assert.ThrowsExceptionAsync<System.Text.Json.JsonException>(() => store.LoadAsync());
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
