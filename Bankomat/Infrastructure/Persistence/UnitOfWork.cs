using Bankomat.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Bankomat.Infrastructure.Persistence;

public class UnitOfWork : IUnitOfWork
{
    private readonly BankomatDbContext _db;

    public UnitOfWork(BankomatDbContext db) => _db = db;

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Состояние контекста после конфликта недостоверно — сбрасываем отслеживание.
            _db.ChangeTracker.Clear();
            throw new ConcurrencyConflictException("Запись была изменена параллельно", ex);
        }
    }
}
