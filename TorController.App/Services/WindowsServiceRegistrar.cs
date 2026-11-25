using System.Diagnostics;
using System.IO;

namespace TorController.App.Services;

public sealed class WindowsServiceRegistrar : IWindowsServiceRegistrar
{
    private readonly Func<ProcessStartInfo, CancellationToken, Task<int>> _runScElevated;

    public WindowsServiceRegistrar(
        Func<ProcessStartInfo, CancellationToken, Task<int>>? runScElevated = null)
    {
        _runScElevated = runScElevated ?? RunScElevatedAsync;
    }

    public async Task RegisterAsync(
        string serviceName,
        string displayName,
        string torExecutablePath,
        string configurationPath,
        string startingType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(torExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);
        ArgumentNullException.ThrowIfNull(startingType);
        if (!Path.IsPathFullyQualified(torExecutablePath) ||
            !Path.IsPathFullyQualified(configurationPath))
        {
            throw new ArgumentException("Executable and configuration paths must be absolute.");
        }

        var startValue = startingType switch
        {
            "Automatic" => "auto",
            "Automatic (Delayed Start)" => "delayed-auto",
            "Manual" => "demand",
            "Disabled" => "disabled",
            _ => throw new ArgumentException("Unsupported Windows service startup type.", nameof(startingType))
        };
        if (new[] { torExecutablePath, configurationPath }
            .Any(value => value.Contains('"')))
        {
            throw new ArgumentException("Service values cannot contain quotation marks.");
        }

        var binaryPath = $"\"{torExecutablePath}\" --nt-service -f \"{configurationPath}\"";
        var startInfo = new ProcessStartInfo
        {
            FileName = "sc.exe",
            UseShellExecute = true,
            Verb = "runas"
        };
        foreach (var argument in new[]
                 {
                     "create",
                     serviceName,
                     "binPath=",
                     binaryPath,
                     "DisplayName=",
                     displayName,
                     "start=",
                     startValue
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var exitCode = await _runScElevated(startInfo, cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Windows service registration failed with exit code {exitCode}.");
        }
    }

    private static async Task<int> RunScElevatedAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows service registration is only available on Windows.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the elevated Windows service controller.");
        await process.WaitForExitAsync(CancellationToken.None);
        return process.ExitCode;
    }
}
