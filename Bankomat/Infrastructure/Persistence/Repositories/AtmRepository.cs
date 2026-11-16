using Bankomat.Domain.Atms;
using Microsoft.EntityFrameworkCore;

namespace Bankomat.Infrastructure.Persistence.Repositories;

public class AtmRepository : IAtmRepository
{
    private readonly BankomatDbContext _db;
    public AtmRepository(BankomatDbContext db) => _db = db;

    // Кассеты — owned-коллекция, EF Core загружает её автоматически, Include не нужен.
    public Task<Atm?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _db.Atms.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<Atm?> GetByIdReadOnlyAsync(int id, CancellationToken ct = default) =>
        _db.Atms.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<bool> AnyAsync(CancellationToken ct = default) =>
        _db.Atms.AnyAsync(ct);

    public async Task AddAsync(Atm atm, CancellationToken ct = default) =>
        await _db.Atms.AddAsync(atm, ct);
}
