using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using TorController.App.Infrastructure;
using TorController.App.Models;
using TorController.App.Services;

namespace TorController.App.Presentation;

public sealed class CreateTorServiceViewModel : INotifyPropertyChanged
{
    private readonly TorConfigParser _configurationParser;
    private string _serviceName = "TorNew";
    private string _displayName = "New Tor Service";
    private string _torExecutablePath = string.Empty;
    private string _dataDirectory = string.Empty;
    private string _startingType = "Manual";
    private string _socksPortsText = string.Empty;
    private bool _useBridges;
    private string _bridgesText = string.Empty;
    private string _clientTransportPluginsText = string.Empty;
    private string _logsText = string.Empty;
    private string _controlPortsText = string.Empty;
    private string _errorMessage = string.Empty;

    public CreateTorServiceViewModel(TorConfigParser? configurationParser = null)
        : this([], new PhysicalFileSystem(), configurationParser)
    {
    }

    public CreateTorServiceViewModel(
        IEnumerable<string> suggestedTorExecutablePaths,
        IFileSystem fileSystem,
        TorConfigParser? configurationParser = null)
    {
        ArgumentNullException.ThrowIfNull(suggestedTorExecutablePaths);
        ArgumentNullException.ThrowIfNull(fileSystem);
        _configurationParser = configurationParser ?? new TorConfigParser();
        SuggestedTorExecutablePaths = suggestedTorExecutablePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Where(path =>
                Path.IsPathFullyQualified(path) &&
                string.Equals(Path.GetFileName(path), "tor.exe", StringComparison.OrdinalIgnoreCase) &&
                fileSystem.FileExists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        SubmitCommand = new AsyncCommand(SubmitAsync);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? RequestSubmitted;

    public IReadOnlyList<string> StartingTypes { get; } =
        ["Automatic", "Automatic (Delayed Start)", "Manual", "Disabled"];

    public IReadOnlyList<string> SuggestedTorExecutablePaths { get; }
    public string ServiceName { get => _serviceName; set => SetField(ref _serviceName, value); }
    public string DisplayName { get => _displayName; set => SetField(ref _displayName, value); }
    public string TorExecutablePath { get => _torExecutablePath; set => SetField(ref _torExecutablePath, value); }
    public string DataDirectory { get => _dataDirectory; set => SetField(ref _dataDirectory, value); }
    public string StartingType { get => _startingType; set => SetField(ref _startingType, value); }
    public string SocksPortsText { get => _socksPortsText; set => SetField(ref _socksPortsText, value); }
    public bool UseBridges { get => _useBridges; set => SetField(ref _useBridges, value); }
    public string BridgesText { get => _bridgesText; set => SetField(ref _bridgesText, value); }
    public string ClientTransportPluginsText { get => _clientTransportPluginsText; set => SetField(ref _clientTransportPluginsText, value); }
    public string LogsText { get => _logsText; set => SetField(ref _logsText, value); }
    public string ControlPortsText { get => _controlPortsText; set => SetField(ref _controlPortsText, value); }
    public string ErrorMessage { get => _errorMessage; private set => SetField(ref _errorMessage, value); }
    public TorServiceCreationRequest? Request { get; private set; }
    public AsyncCommand SubmitCommand { get; }

    public Task SubmitAsync()
    {
        Request = null;
        ErrorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(ServiceName) ||
            string.IsNullOrWhiteSpace(DisplayName) ||
            string.IsNullOrWhiteSpace(TorExecutablePath) ||
            string.IsNullOrWhiteSpace(DataDirectory) ||
            !StartingTypes.Contains(StartingType, StringComparer.Ordinal))
        {
            ErrorMessage = "Enter the service name, display name, tor.exe path, data directory, and startup type.";
            OnPropertyChanged(nameof(Request));
            return Task.CompletedTask;
        }

        try
        {
            var socksPorts = ParsePorts(SocksPortsText, "SocksPort");
            var controlPorts = ParsePorts(ControlPortsText, "ControlPort");
            Request = new TorServiceCreationRequest(
                ServiceName.Trim(),
                DisplayName.Trim(),
                TorExecutablePath.Trim(),
                DataDirectory.Trim(),
                StartingType,
                new TorConfiguration
                {
                    SocksPorts = socksPorts.Ports,
                    SocksPortEndpoints = socksPorts.Endpoints,
                    UseBridges = UseBridges,
                    Bridges = ParseLines(BridgesText),
                    ClientTransportPlugins = ParseLines(ClientTransportPluginsText),
                    Logs = ParseLines(LogsText),
                    ControlPorts = controlPorts.Ports,
                    ControlPortEndpoints = controlPorts.Endpoints
                });
            OnPropertyChanged(nameof(Request));
            RequestSubmitted?.Invoke(this, EventArgs.Empty);
        }
        catch (FormatException exception)
        {
            ErrorMessage = exception.Message;
            OnPropertyChanged(nameof(Request));
        }

        return Task.CompletedTask;
    }

    private (List<int> Ports, List<string> Endpoints) ParsePorts(string text, string directive)
    {
        var entries = text.Split(
            [',', '\r', '\n', ';'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (entries.Length == 0)
        {
            return ([], []);
        }

        var configuration = _configurationParser.Parse(string.Join(
            Environment.NewLine,
            entries.Select(entry => $"{directive} {entry}")));
        return directive == "SocksPort"
            ? (configuration.SocksPorts, configuration.SocksPortEndpoints)
            : (configuration.ControlPorts, configuration.ControlPortEndpoints);
    }

    private static List<string> ParseLines(string text) =>
        text.Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
