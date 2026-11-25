using TorController.App.Presentation;

namespace TorController.Tests.Presentation;

[TestClass]
public sealed class AsyncCommandTests
{
    [TestMethod]
    public async Task ExecuteAsyncRunsDelegateAndRaisesCanExecuteChanged()
    {
        var executed = 0;
        var notifications = 0;
        var command = new AsyncCommand(() =>
        {
            executed++;
            return Task.CompletedTask;
        });
        command.CanExecuteChanged += (_, _) => notifications++;

        await command.ExecuteAsync();

        Assert.AreEqual(1, executed);
        Assert.AreEqual(2, notifications);
    }

    [TestMethod]
    public async Task ExecuteAsyncPreventsConcurrentExecution()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executed = 0;
        var command = new AsyncCommand(() =>
        {
            executed++;
            return completion.Task;
        });

        var firstExecution = command.ExecuteAsync();
        Assert.IsFalse(command.CanExecute(null));
        await command.ExecuteAsync();
        Assert.AreEqual(1, executed);
        completion.SetResult();
        await firstExecution;
        Assert.IsTrue(command.CanExecute(null));
    }

    [TestMethod]
    public void CanExecuteUsesProvidedPredicate()
    {
        var command = new AsyncCommand(() => Task.CompletedTask, () => false);

        Assert.IsFalse(command.CanExecute(null));
    }

    [TestMethod]
    public void ExecuteInvokesAsyncDelegateAndRaiseCanExecuteChangedNotifiesSubscribers()
    {
        var executed = 0;
        var notifications = 0;
        var command = new AsyncCommand(() =>
        {
            executed++;
            return Task.CompletedTask;
        });
        command.CanExecuteChanged += (_, _) => notifications++;

        command.Execute(null);
        command.RaiseCanExecuteChanged();

        Assert.AreEqual(1, executed);
        Assert.AreEqual(3, notifications);
    }
}
