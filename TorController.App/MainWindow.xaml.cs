using System.Windows;
using TorController.App.Presentation;

namespace TorController.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;
    }

    public Task? LoadTask { get; private set; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        LoadTask = _viewModel.OnLoad();
    }
}