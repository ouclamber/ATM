using Bankomat.Domain.ValueObjects;

namespace Bankomat.Domain.Accounts;

public interface IBankAccountRepository
{
    Task<BankAccount?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<BankAccount?> GetByCardNumberAsync(CardNumber cardNumber, CancellationToken ct = default);
    Task AddAsync(BankAccount account, CancellationToken ct = default);
}