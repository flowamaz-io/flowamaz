namespace Flowamaz.Core.Interfaces.Persistence;

/// <summary>
/// Persistence boundary the Application layer depends on instead of EF Core directly
/// (clean architecture: Application never references Infrastructure). Implemented in
/// Infrastructure over the shared scoped DbContext.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>Persist all staged changes. A single call is atomic on its own.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Begin an explicit transaction spanning multiple operations.</summary>
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}

/// <summary>A transaction scope. Dispose without commit rolls back.</summary>
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
