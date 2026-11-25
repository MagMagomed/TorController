namespace TorController.App.Infrastructure;

public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default);
    Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default);
    void CopyFile(string sourcePath, string destinationPath, bool overwrite);
    void MoveFile(string sourcePath, string destinationPath, bool overwrite);
    void DeleteFile(string path);
    void DeleteDirectory(string path);
    bool HasReparsePoints(string path);
}
