using TorController.App.Models;
using System.ComponentModel;
using System.Diagnostics;
using System.ServiceProcess;
using System.Windows;

namespace TorController.App.Services;

public sealed class WindowsServiceManager
{
    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(30);
    private readonly Func<string, WindowsServiceAction, CancellationToken, Task> _directAction;
    private readonly Func<string, WindowsServiceAction, CancellationToken, Task> _elevatedAction;
    private readonly Func<string, CancellationToken, Task<bool>> _confirmElevation;

    public WindowsServiceManager(
        Func<string, WindowsServiceAction, CancellationToken, Task>? directAction = null,
        Func<string, WindowsServiceAction, CancellationToken, Task>? elevatedAction = null,
        Func<string, CancellationToken, Task<bool>>? confirmElevation = null)
    {
        _directAction = directAction ?? ExecuteDirectActionAsync;
        _elevatedAction = elevatedAction ?? ExecuteElevatedActionAsync;
        _confirmElevation = confirmElevation ?? ConfirmElevationAsync;
    }

    public Task<ServiceOperationResult> StartAsync(string serviceName, CancellationToken cancellationToken = default) =>
        ExecuteAsync(serviceName, WindowsServiceAction.Start, cancellationToken);

    public Task<ServiceOperationResult> StopAsync(string serviceName, CancellationToken cancellationToken = default) =>
        ExecuteAsync(serviceName, WindowsServiceAction.Stop, cancellationToken);

    public Task<ServiceOperationResult> RestartAsync(string serviceName, CancellationToken cancellationToken = default) =>
        ExecuteAsync(serviceName, WindowsServiceAction.Restart, cancellationToken);

    public Task<ServiceOperationResult> DeleteAsync(string serviceName, CancellationToken cancellationToken = default) =>
        ExecuteAsync(serviceName, WindowsServiceAction.Delete, cancellationToken);

    private async Task<ServiceOperationResult> ExecuteAsync(
        string serviceName,
        WindowsServiceAction action,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return new ServiceOperationResult(false, "A Windows service name is required.");
        }

        try
        {
            await _directAction(serviceName, action, cancellationToken);
            return new ServiceOperationResult(true, $"Service '{serviceName}' {GetActionText(action)} successfully.");
        }
        catch (Exception exception) when (IsAccessDenied(exception))
        {
            var confirmed = await _confirmElevation(serviceName, cancellationToken);
            if (!confirmed)
            {
                return new ServiceOperationResult(false, "Administrator access was declined; no service changes were made.");
            }

            try
            {
                await _elevatedAction(serviceName, action, cancellationToken);
                return new ServiceOperationResult(true, $"Service '{serviceName}' {GetActionText(action)} successfully.");
            }
            catch (Exception elevatedException) when (IsOperationFailure(elevatedException))
            {
                return new ServiceOperationResult(false, $"Could not {GetActionText(action)} service '{serviceName}': {GetSafeMessage(elevatedException)}");
            }
        }
        catch (Exception exception) when (IsOperationFailure(exception))
        {
            return new ServiceOperationResult(false, $"Could not {GetActionText(action)} service '{serviceName}': {GetSafeMessage(exception)}");
        }
    }

    private static Task ExecuteDirectActionAsync(
        string serviceName,
        WindowsServiceAction action,
        CancellationToken cancellationToken) =>
        Task.Run(() => ExecuteServiceControllerAction(serviceName, action, cancellationToken), cancellationToken);

    private static void ExecuteServiceControllerAction(
        string serviceName,
        WindowsServiceAction action,
        CancellationToken cancellationToken)
    {
        if (action == WindowsServiceAction.Delete)
        {
            RunScAsync("delete", serviceName, cancellationToken).GetAwaiter().GetResult();
            return;
        }

        using var controller = new ServiceController(serviceName);
        controller.Refresh();
        if ((action is WindowsServiceAction.Stop or WindowsServiceAction.Restart) &&
            controller.Status != ServiceControllerStatus.Stopped)
        {
            controller.Stop();
            controller.WaitForStatus(ServiceControllerStatus.Stopped, ServiceTimeout);
            controller.Refresh();
        }

        if ((action is WindowsServiceAction.Start or WindowsServiceAction.Restart) &&
            controller.Status != ServiceControllerStatus.Running)
        {
            controller.Start();
            controller.WaitForStatus(ServiceControllerStatus.Running, ServiceTimeout);
        }
    }

    private static async Task ExecuteElevatedActionAsync(
        string serviceName,
        WindowsServiceAction action,
        CancellationToken cancellationToken)
    {
        if (action == WindowsServiceAction.Delete)
        {
            await RunScElevatedAsync("delete", serviceName, cancellationToken);
            return;
        }

        var currentStatus = GetServiceStatus(serviceName);
        if ((action is WindowsServiceAction.Stop or WindowsServiceAction.Restart) &&
            currentStatus != ServiceControllerStatus.Stopped)
        {
            await RunScElevatedAsync("stop", serviceName, cancellationToken);
            await WaitForStatusAsync(serviceName, ServiceControllerStatus.Stopped, cancellationToken);
            currentStatus = ServiceControllerStatus.Stopped;
        }

        if ((action is WindowsServiceAction.Start or WindowsServiceAction.Restart) &&
            currentStatus != ServiceControllerStatus.Running)
        {
            await RunScElevatedAsync("start", serviceName, cancellationToken);
            await WaitForStatusAsync(serviceName, ServiceControllerStatus.Running, cancellationToken);
        }
    }

    private static ServiceControllerStatus GetServiceStatus(string serviceName)
    {
        using var controller = new ServiceController(serviceName);
        controller.Refresh();
        return controller.Status;
    }

    private static async Task RunScElevatedAsync(
        string operation,
        string serviceName,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "sc.exe",
            UseShellExecute = true,
            Verb = "runas"
        };
        startInfo.ArgumentList.Add(operation);
        startInfo.ArgumentList.Add(serviceName);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the elevated Windows service controller.");
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new Win32Exception(
                process.ExitCode,
                $"Windows service controller exited with code {process.ExitCode}.");
        }
    }

    private static async Task RunScAsync(
        string operation,
        string serviceName,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows service management is only available on Windows.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "sc.exe",
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(operation);
        startInfo.ArgumentList.Add(serviceName);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the Windows service controller.");
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new Win32Exception(
                process.ExitCode,
                $"Windows service controller exited with code {process.ExitCode}.");
        }
    }

    private static async Task WaitForStatusAsync(
        string serviceName,
        ServiceControllerStatus status,
        CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            using var controller = new ServiceController(serviceName);
            controller.WaitForStatus(status, ServiceTimeout);
        }, cancellationToken);
    }

    private static Task<bool> ConfirmElevationAsync(string serviceName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = MessageBox.Show(
            $"Changing Windows service '{serviceName}' requires administrator access. Continue and show the UAC prompt?",
            "Tor Controller",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        return Task.FromResult(result == MessageBoxResult.Yes);
    }

    private static string GetActionText(WindowsServiceAction action) => action switch
    {
        WindowsServiceAction.Start => "started",
        WindowsServiceAction.Stop => "stopped",
        WindowsServiceAction.Restart => "restarted",
        WindowsServiceAction.Delete => "deleted",
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    private static bool IsAccessDenied(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is UnauthorizedAccessException ||
                current is Win32Exception { NativeErrorCode: 5 })
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsOperationFailure(Exception exception) =>
        exception is UnauthorizedAccessException or Win32Exception or InvalidOperationException or
            System.TimeoutException or System.ServiceProcess.TimeoutException or PlatformNotSupportedException;

    private static string GetSafeMessage(Exception exception) =>
        exception is Win32Exception { NativeErrorCode: 1223 }
            ? "The UAC request was cancelled."
            : exception.Message;
}
