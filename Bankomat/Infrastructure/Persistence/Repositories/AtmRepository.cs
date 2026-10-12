using Bankomat.Domain.Atms;
using Microsoft.EntityFrameworkCore;

namespace Bankomat.Infrastructure.Persistence.Repositories;

public class AtmRepository : IAtmRepository
{
    private readonly BankomatDbContext _db;
    public AtmRepository(BankomatDbContext db) => _db = db;

    public Task<Atm?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _db.Atms
            .Include(a => a.Cassettes) // нужен для бизнес-логики
            .FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task AddAsync(Atm atm, CancellationToken ct = default) =>
        await _db.Atms.AddAsync(atm, ct);
}