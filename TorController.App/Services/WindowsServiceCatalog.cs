using TorController.App.Models;
using Microsoft.Win32;
using System.ServiceProcess;
using System.Text.RegularExpressions;

namespace TorController.App.Services;

public sealed class WindowsServiceCatalog : IWindowsServiceCatalog
{
    private static readonly Regex TorNamePattern = new(@"^tor(?:\d+)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TorDisplayNamePattern = new(@"\btor\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TorExecutablePattern = new(@"(?:^|[\\/"" ])tor\.exe(?:[\s""]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly Func<IReadOnlyList<WindowsServiceInfo>> _serviceEnumerator;

    public WindowsServiceCatalog(Func<IReadOnlyList<WindowsServiceInfo>>? serviceEnumerator = null)
    {
        _serviceEnumerator = serviceEnumerator ?? EnumerateWindowsServices;
    }

    public Task<IReadOnlyList<WindowsServiceInfo>> FindTorServicesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var services = _serviceEnumerator()
            .Where(IsTorService)
            .GroupBy(service => service.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        return Task.FromResult<IReadOnlyList<WindowsServiceInfo>>(services);
    }

    public Task<bool> ServiceNameExistsAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        var exists = _serviceEnumerator().Any(service =>
            string.Equals(service.Name, serviceName, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(exists);
    }

    private static bool IsTorService(WindowsServiceInfo service) =>
        TorNamePattern.IsMatch(service.Name) ||
        TorDisplayNamePattern.IsMatch(service.DisplayName) ||
        TorExecutablePattern.IsMatch(service.CommandLine);

    private static IReadOnlyList<WindowsServiceInfo> EnumerateWindowsServices()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows service discovery is only available on Windows.");
        }

        var services = new List<WindowsServiceInfo>();
        foreach (var controller in ServiceController.GetServices())
        {
            using (controller)
            using (var key = Registry.LocalMachine.OpenSubKey(
                       $@"SYSTEM\CurrentControlSet\Services\{controller.ServiceName}"))
            {
                var commandLine = Convert.ToString(key?.GetValue("ImagePath")) ?? string.Empty;
                var displayName = controller.DisplayName;
                if (!TorNamePattern.IsMatch(controller.ServiceName) &&
                    !TorDisplayNamePattern.IsMatch(displayName) &&
                    !TorExecutablePattern.IsMatch(commandLine))
                {
                    continue;
                }

                if (key is null)
                {
                    services.Add(new WindowsServiceInfo(
                        controller.ServiceName,
                        displayName,
                        controller.Status.ToString(),
                        "Unknown",
                        string.Empty,
                        commandLine));
                    continue;
                }

                var startValue = key.GetValue("Start");
                var start = startValue is null ? -1 : Convert.ToInt32(startValue);
                var delayed = Convert.ToInt32(key.GetValue("DelayedAutoStart") ?? 0) == 1;
                var startingType = start switch
                {
                    2 when delayed => "Automatic (Delayed Start)",
                    2 => "Automatic",
                    3 => "Manual",
                    4 => "Disabled",
                    _ => "Unknown"
                };
                var serviceAccount = Convert.ToString(key.GetValue("ObjectName")) ?? "LocalSystem";
                services.Add(new WindowsServiceInfo(
                    controller.ServiceName,
                    displayName,
                    controller.Status.ToString(),
                    startingType,
                    serviceAccount,
                    Environment.ExpandEnvironmentVariables(commandLine)));
            }
        }

        return services;
    }
}
