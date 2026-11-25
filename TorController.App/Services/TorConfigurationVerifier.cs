using TorController.App.Models;
using System.Diagnostics;
using System.IO;

namespace TorController.App.Services;

public sealed class TorConfigurationVerifier
{
    private readonly Func<string, string, CancellationToken, Task<int>> _runTor;

    public TorConfigurationVerifier(
        Func<string, string, CancellationToken, Task<int>>? runTor = null)
    {
        _runTor = runTor ?? RunTorAsync;
    }

    public async Task<ConfigurationVerificationResult> VerifyAsync(
        string torExecutablePath,
        string configurationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(torExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);

        var exitCode = await _runTor(torExecutablePath, configurationPath, cancellationToken);
        return exitCode == 0
            ? new ConfigurationVerificationResult(true, "Tor configuration is valid.")
            : new ConfigurationVerificationResult(
                false,
                $"Tor configuration validation failed with exit code {exitCode}.");
    }

    private static async Task<int> RunTorAsync(
        string executablePath,
        string configurationPath,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--verify-config");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add(configurationPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the Tor configuration verifier.");
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}
