using Bankomat.Application.Abstractions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bankomat.Infrastructure.Persistence;

public class UnitOfWork : IUnitOfWork
{
    private readonly BankomatDbContext _db;
    private IDbContextTransaction? _tx;

    public UnitOfWork(BankomatDbContext db) => _db = db;

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    public async Task BeginTransactionAsync(CancellationToken ct = default)
    {
        if (_tx is not null) return;
        _tx = await _db.Database.BeginTransactionAsync(ct);
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_tx is null) return;
        await _tx.CommitAsync(ct);
        await _tx.DisposeAsync();
        _tx = null;
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_tx is null) return;
        await _tx.RollbackAsync(ct);
        await _tx.DisposeAsync();
        _tx = null;
    }
}