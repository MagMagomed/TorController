using System.ComponentModel;
using TorController.App.Models;
using TorController.App.Services;

namespace TorController.Tests.Services;

[TestClass]
public sealed class WindowsServiceManagerTests
{
    [TestMethod]
    public async Task StartAsyncExecutesStartAction()
    {
        var calls = new List<WindowsServiceAction>();
        var manager = new WindowsServiceManager((_, action, _) =>
        {
            calls.Add(action);
            return Task.CompletedTask;
        });

        var result = await manager.StartAsync("tor");

        Assert.IsTrue(result.Success);
        CollectionAssert.AreEqual(new[] { WindowsServiceAction.Start }, calls);
    }

    [TestMethod]
    public async Task StopAsyncExecutesStopAction()
    {
        var calls = new List<WindowsServiceAction>();
        var manager = new WindowsServiceManager((_, action, _) =>
        {
            calls.Add(action);
            return Task.CompletedTask;
        });

        var result = await manager.StopAsync("tor");

        Assert.IsTrue(result.Success);
        CollectionAssert.AreEqual(new[] { WindowsServiceAction.Stop }, calls);
    }

    [TestMethod]
    public async Task RestartAsyncExecutesRestartAction()
    {
        var calls = new List<WindowsServiceAction>();
        var manager = new WindowsServiceManager((_, action, _) =>
        {
            calls.Add(action);
            return Task.CompletedTask;
        });

        var result = await manager.RestartAsync("tor");

        Assert.IsTrue(result.Success);
        CollectionAssert.AreEqual(new[] { WindowsServiceAction.Restart }, calls);
    }

    [TestMethod]
    public async Task DeleteAsyncExecutesDeleteAction()
    {
        var calls = new List<WindowsServiceAction>();
        var manager = new WindowsServiceManager((_, action, _) =>
        {
            calls.Add(action);
            return Task.CompletedTask;
        });

        var result = await manager.DeleteAsync("tor");

        Assert.IsTrue(result.Success);
        CollectionAssert.AreEqual(new[] { WindowsServiceAction.Delete }, calls);
    }

    [TestMethod]
    public async Task DeleteAsyncRejectsEmptyServiceName()
    {
        var manager = new WindowsServiceManager();

        var result = await manager.DeleteAsync(" ");

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "name is required");
    }

    [TestMethod]
    public async Task DeleteAsyncPromptsForElevationWhenDirectDeletionIsDenied()
    {
        var elevatedCalls = 0;
        var elevationPrompts = 0;
        var manager = new WindowsServiceManager(
            (_, _, _) => Task.FromException(new Win32Exception(5)),
            (_, action, _) =>
            {
                Assert.AreEqual(WindowsServiceAction.Delete, action);
                elevatedCalls++;
                return Task.CompletedTask;
            },
            (_, _) =>
            {
                elevationPrompts++;
                return Task.FromResult(true);
            });

        var result = await manager.DeleteAsync("tor");

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, elevationPrompts);
        Assert.AreEqual(1, elevatedCalls);
    }

    [TestMethod]
    public async Task DeleteAsyncReportsCancelledUacWithoutDeletingService()
    {
        var manager = new WindowsServiceManager(
            (_, _, _) => Task.FromException(new Win32Exception(5)),
            (_, _, _) => Task.FromException(new Win32Exception(1223)),
            (_, _) => Task.FromResult(true));

        var result = await manager.DeleteAsync("tor");

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "UAC request was cancelled");
    }

    [TestMethod]
    public async Task AccessDeniedPromptsAndUsesElevatedActionWhenAccepted()
    {
        var elevatedCalls = 0;
        var manager = new WindowsServiceManager(
            (_, _, _) => Task.FromException(new Win32Exception(5)),
            (_, _, _) =>
            {
                elevatedCalls++;
                return Task.CompletedTask;
            },
            (_, _) => Task.FromResult(true));

        var result = await manager.StartAsync("tor");

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, elevatedCalls);
    }

    [TestMethod]
    public async Task WrappedAccessDeniedPromptsForElevation()
    {
        var prompts = 0;
        var manager = new WindowsServiceManager(
            (_, _, _) => Task.FromException(new InvalidOperationException("service operation failed", new Win32Exception(5))),
            (_, _, _) => Task.CompletedTask,
            (_, _) =>
            {
                prompts++;
                return Task.FromResult(false);
            });

        var result = await manager.StartAsync("tor");

        Assert.IsFalse(result.Success);
        Assert.AreEqual(1, prompts);
    }

    [TestMethod]
    public async Task DeclinedElevationDoesNotExecutePrivilegedAction()
    {
        var elevatedCalls = 0;
        var manager = new WindowsServiceManager(
            (_, _, _) => Task.FromException(new UnauthorizedAccessException("access denied")),
            (_, _, _) =>
            {
                elevatedCalls++;
                return Task.CompletedTask;
            },
            (_, _) => Task.FromResult(false));

        var result = await manager.StopAsync("tor");

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, elevatedCalls);
    }

    [TestMethod]
    public async Task ReportsNonPermissionFailuresWithoutRequestingElevation()
    {
        var prompts = 0;
        var manager = new WindowsServiceManager(
            (_, _, _) => Task.FromException(new InvalidOperationException("service not found")),
            confirmElevation: (_, _) =>
            {
                prompts++;
                return Task.FromResult(true);
            });

        var result = await manager.RestartAsync("missing");

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Message.Contains("service not found", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(0, prompts);
    }
}
