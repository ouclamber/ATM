namespace Bankomat.Domain.Atms;

public interface IAtmRepository
{
    Task<Atm?> GetByIdAsync(int id, CancellationToken ct = default);
    Task AddAsync(Atm atm, CancellationToken ct = default);
}