using Bankomat.Domain.Accounts;
using Bankomat.Domain.Common;

namespace Bankomat.Application.Accounts;

/// <summary>Актуальный баланс и статус счёта из БД (а не из кеша сессии).</summary>
public class GetAccountSummaryHandler
{
    private readonly IBankAccountRepository _accounts;

    public GetAccountSummaryHandler(IBankAccountRepository accounts) => _accounts = accounts;

    public async Task<Result<AccountSummary>> HandleAsync(int accountId, CancellationToken ct = default)
    {
        var account = await _accounts.GetByIdAsync(accountId, ct);
        return account is null
            ? Error.NotFound("Счет не найден")
            : AccountSummary.From(account);
    }
}
