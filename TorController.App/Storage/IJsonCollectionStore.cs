namespace TorController.App.Storage;

public interface IJsonCollectionStore<T>
{
    Task<IReadOnlyList<T>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IReadOnlyCollection<T> items, CancellationToken cancellationToken = default);
}
