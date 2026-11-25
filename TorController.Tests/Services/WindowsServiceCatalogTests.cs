using TorController.App.Models;
using TorController.App.Services;

namespace TorController.Tests.Services;

[TestClass]
public sealed class WindowsServiceCatalogTests
{
    [TestMethod]
    public async Task FindTorServicesAsyncReturnsServicesWithTorExecutable()
    {
        var catalog = new WindowsServiceCatalog(() =>
        [
            CreateService("tor", "Tor Win32 Service", "\"C:\\Tor\\tor.exe\" -f C:\\Tor\\torrc"),
            CreateService("Tor2", "Tor2", "C:\\Tor\\tor.exe --nt-service"),
            CreateService("other", "Other service", "C:\\Other\\other.exe")
        ]);

        var result = await catalog.FindTorServicesAsync();

        CollectionAssert.AreEqual(new[] { "tor", "Tor2" }, result.Select(item => item.Name).ToArray());
    }

    [TestMethod]
    public async Task FindTorServicesAsyncUsesServiceNameOrDisplayNameWhenCommandLineUnavailable()
    {
        var catalog = new WindowsServiceCatalog(() =>
        [
            CreateService("Tor3", "Relay", string.Empty),
            CreateService("unrelated", "Tor Helper", string.Empty),
            CreateService("monitor", "Service Monitor", string.Empty)
        ]);

        var result = await catalog.FindTorServicesAsync();

        CollectionAssert.AreEqual(new[] { "Tor3", "unrelated" }, result.Select(item => item.Name).ToArray());
    }

    [TestMethod]
    public async Task FindTorServicesAsyncPropagatesInventoryFailures()
    {
        var catalog = new WindowsServiceCatalog(() => throw new InvalidOperationException("inventory unavailable"));

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => catalog.FindTorServicesAsync());
    }

    [TestMethod]
    public async Task ServiceNameExistsAsyncChecksAllServicesIgnoringCase()
    {
        var catalog = new WindowsServiceCatalog(() =>
        [
            CreateService("UnrelatedSvc", "Unrelated", "C:\\Other\\other.exe")
        ]);

        Assert.IsTrue(await catalog.ServiceNameExistsAsync("unrelatedsvc"));
        Assert.IsFalse(await catalog.ServiceNameExistsAsync("missing"));
    }

    [TestMethod]
    public async Task ServiceNameExistsAsyncValidatesNameAndCancellation()
    {
        var catalog = new WindowsServiceCatalog(() => []);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExceptionAsync<ArgumentException>(() => catalog.ServiceNameExistsAsync(" "));
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
            catalog.ServiceNameExistsAsync("tor", cancellation.Token));
    }

    private static WindowsServiceInfo CreateService(string name, string displayName, string commandLine) =>
        new(name, displayName, "Running", "Automatic", "LocalSystem", commandLine);
}
