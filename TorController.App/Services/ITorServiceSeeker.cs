using TorController.App.Models;

namespace TorController.App.Services;

public interface ITorServiceSeeker
{
    Task<SeekResult> Execute(CancellationToken cancellationToken = default);
}
