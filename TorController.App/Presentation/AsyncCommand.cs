using System.Windows.Input;

namespace TorController.App.Presentation;

public sealed class AsyncCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool> _canExecute;
    private bool _isExecuting;

    public AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute ?? (() => true);
    }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_isExecuting && _canExecute();

    public async void Execute(object? parameter)
    {
        if (CanExecute(parameter))
        {
            await ExecuteAsync();
        }
    }

    public async Task ExecuteAsync()
    {
        if (_isExecuting || !_canExecute())
        {
            return;
        }

        _isExecuting = true;
        RaiseCanExecuteChanged();
        try
        {
            await _execute();
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
