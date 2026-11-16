namespace Bankomat.Application.Abstractions;

/// <summary>
/// Граница транзакции сценария. Один вызов <see cref="SaveChangesAsync"/> атомарен
/// (EF Core оборачивает его в транзакцию БД), поэтому явные Begin/Commit не нужны.
/// </summary>
public interface IUnitOfWork
{
    /// <exception cref="ConcurrencyConflictException">
    /// Данные были изменены параллельно (другим банкоматом/сеансом) после чтения.
    /// </exception>
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
