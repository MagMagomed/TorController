using System.Windows;
using System.Windows.Threading;

namespace TorController.Tests.Presentation;

[TestClass]
[DoNotParallelize]
public sealed class AppTests
{
    [TestMethod]
    public async Task StartApplicationAsyncRejectsMissingDependencies()
    {
        Func<Task<Window>> createWindow = () => Task.FromResult<Window>(new Window());
        Action<Window> setMainWindow = _ => { };
        Action<Window> showWindow = _ => { };
        Action<string> showError = _ => { };
        Action<int> shutdown = _ => { };

        await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
            TorController.App.App.StartApplicationAsync(null!, setMainWindow, showWindow, showError, shutdown));
        await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
            TorController.App.App.StartApplicationAsync(createWindow, null!, showWindow, showError, shutdown));
        await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
            TorController.App.App.StartApplicationAsync(createWindow, setMainWindow, null!, showError, shutdown));
        await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
            TorController.App.App.StartApplicationAsync(createWindow, setMainWindow, showWindow, null!, shutdown));
        await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
            TorController.App.App.StartApplicationAsync(createWindow, setMainWindow, showWindow, showError, null!));
    }

    [TestMethod]
    public void StartApplicationAsyncShowsWindowAndReportsStartupFailures()
    {
        RunOnSta(async () =>
        {
            var createCount = 0;
            var mainWindows = new List<Window>();
            var shownWindows = new List<Window>();
            var errors = new List<string>();
            var shutdownCodes = new List<int>();
            Task<Window> CreateWindow()
            {
                createCount++;
                return createCount == 1
                    ? Task.FromResult<Window>(new Window())
                    : Task.FromException<Window>(new IOException("mock startup failure"));
            }

            await TorController.App.App.StartApplicationAsync(
                CreateWindow,
                mainWindows.Add,
                shownWindows.Add,
                errors.Add,
                shutdownCodes.Add);

            Assert.AreEqual(1, shownWindows.Count);
            Assert.AreSame(mainWindows.Single(), shownWindows[0]);
            Assert.AreEqual(0, errors.Count);
            Assert.AreEqual(0, shutdownCodes.Count);

            await TorController.App.App.StartApplicationAsync(
                CreateWindow,
                mainWindows.Add,
                shownWindows.Add,
                errors.Add,
                shutdownCodes.Add);

            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains(errors[0], "mock startup failure");
            CollectionAssert.AreEqual(new[] { -1 }, shutdownCodes);
            shownWindows[0].Close();
        });
    }

    private static void RunOnSta(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                var frame = new DispatcherFrame();
                var task = action();
                _ = task.ContinueWith(
                    _ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
                Dispatcher.PushFrame(frame);
                task.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw failure;
        }
    }
}
