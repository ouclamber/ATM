using Bankomat.Application.Abstractions;
using Bankomat.Application.Common;
using Bankomat.Domain.Accounts;
using Bankomat.Domain.Atms;
using Bankomat.Domain.Common;
using Bankomat.Domain.Transactions;
using Bankomat.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Bankomat.Application.Operations;

public class DepositHandler
{
    private readonly IBankAccountRepository _accounts;
    private readonly IAtmRepository _atms;
    private readonly ITransactionRepository _transactions;
    private readonly IBankConfig _config;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<DepositHandler> _logger;

    public DepositHandler(
        IBankAccountRepository accounts,
        IAtmRepository atms,
        ITransactionRepository transactions,
        IBankConfig config,
        IUnitOfWork uow,
        ILogger<DepositHandler> logger)
    {
        _accounts = accounts;
        _atms = atms;
        _transactions = transactions;
        _config = config;
        _uow = uow;
        _logger = logger;
    }

    /// <param name="amountValue">Заявленная сумма; должна точно совпасть с суммой купюр.</param>
    /// <param name="banknotes">Номинал → количество купюр.</param>
    public async Task<Result<Money>> HandleAsync(
        int accountId, int atmId, decimal amountValue,
        IReadOnlyDictionary<int, int> banknotes,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Deposit: начало операции, счёт {AccountId}, банкомат {AtmId}, сумма {Amount}, купюры: {Banknotes}",
            accountId, atmId, amountValue, LogFormat.Banknotes(banknotes));

        var amountResult = Money.Create(amountValue);
        if (amountResult.IsFailure)
            return Reject(accountId, amountResult.Error);
        var amount = amountResult.Value;

        if (amount <= Money.Zero)
            return Reject(accountId, Error.Validation("Сумма должна быть больше нуля"));

        if (amount.Amount > _config.MaxDepositPerTransaction)
            return Reject(accountId, Error.Business(
                $"Лимит за операцию: {_config.MaxDepositPerTransaction:C}"));

        // Раньше отрицательное количество проходило проверку суммы (5000×1 + 1000×(-1) = 4000),
        // а банкомат молча игнорировал «-1»: в кассету попадало 5000, на счёт — 4000.
        foreach (var (denomination, count) in banknotes)
        {
            if (count < 0)
                return Reject(accountId, Error.Validation("Количество купюр не может быть отрицательным"));
            if (Denomination.Create(denomination).IsFailure)
                return Reject(accountId, Error.Validation($"Недопустимый номинал: {denomination}"));
        }

        var banknotesTotal = banknotes.Sum(x => (decimal)x.Key * x.Value);
        if (banknotesTotal != amount.Amount)
            return Reject(accountId, Error.Validation(
                $"Сумма купюр ({banknotesTotal:C}) не совпадает с заявленной ({amount})"));

        var account = await _accounts.GetByIdAsync(accountId, ct);
        if (account is null)
            return Reject(accountId, Error.NotFound("Счет не найден"));

        var atm = await _atms.GetByIdAsync(atmId, ct);
        if (atm is null)
            return Reject(accountId, Error.NotFound("Банкомат не найден"));

        if (!atm.IsActive)
            return Reject(accountId, Error.Business("Банкомат неактивен"));

        var depositResult = atm.DepositBanknotes(banknotes);
        if (depositResult.IsFailure)
            return Reject(accountId, depositResult.Error);

        var balanceBefore = account.Balance;
        var creditResult = account.Credit(amount);
        if (creditResult.IsFailure)
            return Reject(accountId, creditResult.Error);

        await _transactions.AddAsync(Transaction.Record(
            account.Id, atm.Id, TransactionType.Deposit,
            amount, balanceBefore, account.Balance,
            $"Внесение {amount}"), ct);

        var saved = await _uow.TrySaveChangesAsync(ct);
        if (saved.IsFailure)
            return Reject(accountId, saved.Error);

        _logger.LogInformation(
            "Deposit: успешно, счёт {AccountId}, банкомат {AtmId}, сумма {Amount}",
            accountId, atmId, amount);

        return account.Balance;
    }

    private Error Reject(int accountId, Error error)
    {
        _logger.LogWarning(
            "Deposit: отклонено, счёт {AccountId}, код {Code}, причина: {Reason}",
            accountId, error.Code, error.Message);
        return error;
    }
}
