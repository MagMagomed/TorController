using TorController.App.Infrastructure;
using TorController.App.Presentation;

namespace TorController.Tests.Presentation;

[TestClass]
public sealed class CreateTorServiceViewModelTests
{
    [TestMethod]
    public async Task SubmitAsyncBuildsRequestAndRaisesSubmittedEvent()
    {
        var viewModel = CreateViewModel();
        SetRequiredValues(viewModel);
        viewModel.SocksPortsText = "127.0.0.1:9060";
        viewModel.UseBridges = true;
        viewModel.BridgesText = "obfs4 192.0.2.1:443 FINGERPRINT";
        viewModel.ClientTransportPluginsText = @"obfs4 exec C:\Tor\lyrebird.exe";
        viewModel.LogsText = "notice file notices.log";
        viewModel.ControlPortsText = "127.0.0.1:9061";
        var submitted = 0;
        viewModel.RequestSubmitted += (_, _) => submitted++;

        await viewModel.SubmitAsync();

        Assert.AreEqual(1, submitted);
        Assert.IsNotNull(viewModel.Request);
        Assert.AreEqual("TorNew", viewModel.Request.ServiceName);
        Assert.AreEqual("Manual", viewModel.Request.StartingType);
        CollectionAssert.AreEqual(new[] { "127.0.0.1:9060" }, viewModel.Request.Configuration.SocksPortEndpoints);
        CollectionAssert.AreEqual(new[] { "127.0.0.1:9061" }, viewModel.Request.Configuration.ControlPortEndpoints);
        Assert.IsTrue(viewModel.Request.Configuration.UseBridges);
        CollectionAssert.AreEqual(new[] { "obfs4 192.0.2.1:443 FINGERPRINT" }, viewModel.Request.Configuration.Bridges);
        Assert.AreEqual(string.Empty, viewModel.ErrorMessage);
    }

    [TestMethod]
    public async Task SubmitAsyncRequiresServiceFields()
    {
        var viewModel = new CreateTorServiceViewModel();
        var submitted = 0;
        viewModel.RequestSubmitted += (_, _) => submitted++;

        await viewModel.SubmitAsync();

        Assert.AreEqual(0, submitted);
        Assert.IsNull(viewModel.Request);
        Assert.IsFalse(string.IsNullOrWhiteSpace(viewModel.ErrorMessage));
    }

    [TestMethod]
    public async Task SubmitAsyncReportsMalformedPortsAndDoesNotSubmit()
    {
        var viewModel = CreateViewModel();
        SetRequiredValues(viewModel);
        viewModel.SocksPortsText = "not-a-port";
        var submitted = 0;
        viewModel.RequestSubmitted += (_, _) => submitted++;

        await viewModel.SubmitAsync();

        Assert.AreEqual(0, submitted);
        Assert.IsNull(viewModel.Request);
        StringAssert.Contains(viewModel.ErrorMessage, "SocksPort");
    }

    [TestMethod]
    public async Task SubmitCommandRunsRequestPreparation()
    {
        var viewModel = CreateViewModel();
        SetRequiredValues(viewModel);

        await viewModel.SubmitCommand.ExecuteAsync();

        Assert.IsNotNull(viewModel.Request);
    }

    [TestMethod]
    public void PropertyChangesRaiseNotificationsOnlyWhenValuesChange()
    {
        var viewModel = CreateViewModel();
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        viewModel.ServiceName = "TorNew2";
        viewModel.ServiceName = "TorNew2";
        viewModel.UseBridges = true;

        CollectionAssert.AreEqual(new[] { "ServiceName", "UseBridges" }, changedProperties);
    }

    [TestMethod]
    public void ConstructorOffersOnlyExistingUniqueExecutablePaths()
    {
        var viewModel = new CreateTorServiceViewModel(
            [
                @"C:\Tor\tor.exe",
                @"c:\tor\TOR.EXE",
                @"D:\Missing\tor.exe",
                @"relative\tor.exe"
            ],
            new ExistingFilesFileSystem(@"C:\Tor\tor.exe"));

        CollectionAssert.AreEqual(
            new[] { @"C:\Tor\tor.exe" },
            viewModel.SuggestedTorExecutablePaths.ToArray());
    }

    [TestMethod]
    public void ConstructorLeavesSuggestionsEmptyWhenNoExecutableExists()
    {
        var viewModel = new CreateTorServiceViewModel(
            [@"D:\Missing\tor.exe"],
            new ExistingFilesFileSystem());

        Assert.AreEqual(0, viewModel.SuggestedTorExecutablePaths.Count);
    }

    private static CreateTorServiceViewModel CreateViewModel() => new();

    private static void SetRequiredValues(CreateTorServiceViewModel viewModel)
    {
        viewModel.ServiceName = "TorNew";
        viewModel.DisplayName = "New Tor Service";
        viewModel.TorExecutablePath = @"C:\Tor\tor.exe";
        viewModel.DataDirectory = @"C:\Tor\NewData";
    }

    private sealed class ExistingFilesFileSystem(params string[] existingFiles) : IFileSystem
    {
        private readonly HashSet<string> _existingFiles = new(existingFiles, StringComparer.OrdinalIgnoreCase);

        public bool FileExists(string path) => _existingFiles.Contains(path);
        public bool DirectoryExists(string path) => throw new NotSupportedException();
        public void CreateDirectory(string path) => throw new NotSupportedException();
        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public void CopyFile(string sourcePath, string destinationPath, bool overwrite) =>
            throw new NotSupportedException();
        public void MoveFile(string sourcePath, string destinationPath, bool overwrite) =>
            throw new NotSupportedException();
        public void DeleteFile(string path) => throw new NotSupportedException();
        public void DeleteDirectory(string path) => throw new NotSupportedException();
        public bool HasReparsePoints(string path) => false;
    }
}
