using TorController.App.Infrastructure;

namespace TorController.Tests.Infrastructure;

[TestClass]
public sealed class PhysicalFileSystemTests
{
    [TestMethod]
    public async Task FileOperationsRoundTripWithinTemporaryDirectory()
    {
        using var directory = new TemporaryDirectory();
        var fileSystem = new PhysicalFileSystem();
        var source = Path.Combine(directory.Path, "source.txt");
        var copied = Path.Combine(directory.Path, "copied.txt");
        var moved = Path.Combine(directory.Path, "moved.txt");

        var createdDirectory = Path.Combine(directory.Path, "created");
        Assert.IsFalse(fileSystem.DirectoryExists(createdDirectory));
        fileSystem.CreateDirectory(createdDirectory);
        Assert.IsTrue(fileSystem.DirectoryExists(createdDirectory));

        await fileSystem.WriteAllTextAsync(source, "mock data");
        Assert.IsTrue(fileSystem.FileExists(source));
        Assert.AreEqual("mock data", await fileSystem.ReadAllTextAsync(source));

        fileSystem.CopyFile(source, copied, overwrite: false);
        Assert.AreEqual("mock data", await fileSystem.ReadAllTextAsync(copied));
        fileSystem.MoveFile(copied, moved, overwrite: false);
        Assert.IsFalse(fileSystem.FileExists(copied));
        Assert.IsTrue(fileSystem.FileExists(moved));
        fileSystem.DeleteFile(moved);
        Assert.IsFalse(fileSystem.FileExists(moved));
    }

    [TestMethod]
    public async Task DeleteDirectoryRemovesNestedContentsWithoutTouchingSibling()
    {
        using var directory = new TemporaryDirectory();
        var fileSystem = new PhysicalFileSystem();
        var target = Path.Combine(directory.Path, "target");
        var nested = Path.Combine(target, "nested");
        var sibling = Path.Combine(directory.Path, "sibling");
        Directory.CreateDirectory(nested);
        Directory.CreateDirectory(sibling);
        await File.WriteAllTextAsync(Path.Combine(nested, "state.dat"), "test data");
        await File.WriteAllTextAsync(Path.Combine(sibling, "keep.dat"), "keep");

        fileSystem.DeleteDirectory(target);

        Assert.IsFalse(fileSystem.DirectoryExists(target));
        Assert.IsTrue(fileSystem.FileExists(Path.Combine(sibling, "keep.dat")));
        Assert.IsFalse(fileSystem.HasReparsePoints(sibling));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
