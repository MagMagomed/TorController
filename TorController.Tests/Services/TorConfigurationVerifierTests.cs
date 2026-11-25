using TorController.App.Services;

namespace TorController.Tests.Services;

[TestClass]
public sealed class TorConfigurationVerifierTests
{
    [TestMethod]
    public async Task VerifyAsyncPassesExecutableAndConfigToVerifierAndReportsSuccess()
    {
        string? executable = null;
        string? configuration = null;
        var verifier = new TorConfigurationVerifier((exe, torrc, _) =>
        {
            executable = exe;
            configuration = torrc;
            return Task.FromResult(0);
        });

        var result = await verifier.VerifyAsync(@"C:\Tor\tor.exe", @"C:\Tor\candidate.torrc");

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(@"C:\Tor\tor.exe", executable);
        Assert.AreEqual(@"C:\Tor\candidate.torrc", configuration);
    }

    [TestMethod]
    public async Task VerifyAsyncReportsNonZeroExitWithoutExposingProcessOutput()
    {
        var verifier = new TorConfigurationVerifier((_, _, _) => Task.FromResult(1));

        var result = await verifier.VerifyAsync(@"C:\Tor\tor.exe", @"C:\Tor\candidate.torrc");

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Message.Contains("exit code 1", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(result.Message.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task VerifyAsyncPropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var verifier = new TorConfigurationVerifier((_, _, token) =>
            Task.FromCanceled<int>(token));

        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() =>
            verifier.VerifyAsync(@"C:\Tor\tor.exe", @"C:\Tor\candidate.torrc", cancellation.Token));
    }
}
