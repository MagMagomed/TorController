using System.Windows;
using TorController.App.Presentation;

namespace TorController.App;

public partial class CreateTorServiceWindow : Window
{
    public CreateTorServiceWindow(CreateTorServiceViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestSubmitted += OnRequestSubmitted;
    }

    private void OnRequestSubmitted(object? sender, EventArgs e)
    {
        if (sender is CreateTorServiceViewModel { Request: not null })
        {
            DialogResult = true;
        }
    }
}
