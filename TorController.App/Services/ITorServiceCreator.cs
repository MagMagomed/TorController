using TorController.App.Models;

namespace TorController.App.Services;

public interface ITorServiceCreator
{
    Task<TorServiceCreationResult> CreateAsync(
        TorServiceCreationRequest request,
        CancellationToken cancellationToken = default);
}
