using System.ComponentModel;
using System.Diagnostics;
using TorController.App.Services;

namespace TorController.Tests.Services;

[TestClass]
public sealed class WindowsServiceRegistrarTests
{
    [TestMethod]
    public async Task RegisterAsyncBuildsScCreateArgumentsForManualStartup()
    {
        ProcessStartInfo? startInfo = null;
        var registrar = new WindowsServiceRegistrar((info, _) =>
        {
            startInfo = info;
            return Task.FromResult(0);
        });

        await registrar.RegisterAsync(
            "TorNew",
            "New Tor Service",
            @"C:\Program Files\Tor\tor.exe",
            @"C:\Tor Data\torrc",
            "Manual");

        CollectionAssert.AreEqual(
            new[]
            {
                "create", "TorNew", "binPath=",
                "\"C:\\Program Files\\Tor\\tor.exe\" --nt-service -f \"C:\\Tor Data\\torrc\"",
                "DisplayName=", "New Tor Service", "start=", "demand"
            },
            startInfo!.ArgumentList.ToArray());
        Assert.AreEqual("sc.exe", startInfo.FileName);
        Assert.IsTrue(startInfo.UseShellExecute);
        Assert.AreEqual("runas", startInfo.Verb);
    }

    [DataTestMethod]
    [DataRow("Automatic", "auto")]
    [DataRow("Automatic (Delayed Start)", "delayed-auto")]
    [DataRow("Disabled", "disabled")]
    public async Task RegisterAsyncMapsSupportedStartupTypes(string startingType, string scStartType)
    {
        ProcessStartInfo? startInfo = null;
        var registrar = new WindowsServiceRegistrar((info, _) =>
        {
            startInfo = info;
            return Task.FromResult(0);
        });

        await registrar.RegisterAsync("TorNew", "New Tor Service", @"C:\Tor\tor.exe", @"C:\Tor\torrc", startingType);

        Assert.AreEqual(scStartType, startInfo!.ArgumentList[^1]);
    }

    [TestMethod]
    public async Task RegisterAsyncReportsScFailure()
    {
        var registrar = new WindowsServiceRegistrar((_, _) => Task.FromResult(1073));

        var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            registrar.RegisterAsync("TorNew", "New Tor Service", @"C:\Tor\tor.exe", @"C:\Tor\torrc", "Manual"));

        StringAssert.Contains(exception.Message, "1073");
    }

    [TestMethod]
    public async Task RegisterAsyncRejectsInvalidInputsBeforeLaunchingSc()
    {
        var calls = 0;
        var registrar = new WindowsServiceRegistrar((_, _) =>
        {
            calls++;
            return Task.FromResult(0);
        });

        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            registrar.RegisterAsync("", "Display", @"C:\Tor\tor.exe", @"C:\Tor\torrc", "Manual"));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            registrar.RegisterAsync("TorNew", "", @"C:\Tor\tor.exe", @"C:\Tor\torrc", "Manual"));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            registrar.RegisterAsync("TorNew", "Display", "", @"C:\Tor\torrc", "Manual"));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            registrar.RegisterAsync("TorNew", "Display", @"C:\Tor\tor.exe", "", "Manual"));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            registrar.RegisterAsync("TorNew", "Display", "relative-tor.exe", @"C:\Tor\torrc", "Manual"));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            registrar.RegisterAsync("TorNew", "Display", @"C:\Tor\tor.exe", @"C:\Tor\torrc", "Unknown"));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            registrar.RegisterAsync("TorNew", "Display", "C:\\Tor\\\"bad.exe", @"C:\Tor\torrc", "Manual"));

        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public async Task RegisterAsyncPropagatesUacCancellation()
    {
        var registrar = new WindowsServiceRegistrar((_, _) =>
            Task.FromException<int>(new Win32Exception(1223)));

        await Assert.ThrowsExceptionAsync<Win32Exception>(() =>
            registrar.RegisterAsync("TorNew", "New Tor Service", @"C:\Tor\tor.exe", @"C:\Tor\torrc", "Manual"));
    }

    [TestMethod]
    public async Task RegisterAsyncHonorsCancellationBeforeRequestingUac()
    {
        var calls = 0;
        var registrar = new WindowsServiceRegistrar((_, _) =>
        {
            calls++;
            return Task.FromResult(0);
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
            registrar.RegisterAsync(
                "TorNew", "New Tor Service", @"C:\Tor\tor.exe", @"C:\Tor\torrc", "Manual", cancellation.Token));

        Assert.AreEqual(0, calls);
    }
}
