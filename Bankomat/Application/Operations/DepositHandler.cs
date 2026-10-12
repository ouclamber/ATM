using Bankomat.Application.Abstractions;
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

    public async Task<Result<Money>> HandleAsync(
        int accountId, int atmId, decimal amountValue,
        IReadOnlyDictionary<int, int> banknotes,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Deposit: начало операции, счёт {AccountId}, банкомат {AtmId}, сумма {Amount}, купюры: {Banknotes}",
            accountId, atmId, amountValue, FormatBanknotes(banknotes));

        var amountResult = Money.Create(amountValue);
        if (amountResult.IsFailure)
        {
            _logger.LogWarning(
                "Deposit: невалидная сумма {Amount} — {Reason}",
                amountValue, amountResult.Error.Message);
            return amountResult.Error;
        }
        var amount = amountResult.Value;

        if (amount.Amount > _config.MaxDepositPerTransaction)
        {
            _logger.LogWarning(
                "Deposit: превышен лимит за операцию, счёт {AccountId}, сумма {Amount}, лимит {Limit}",
                accountId, amount, _config.MaxDepositPerTransaction);

            return Error.Business(
                $"Лимит за операцию: {_config.MaxDepositPerTransaction:C}");
        }

        var banknotesTotal = banknotes.Sum(x => (decimal)x.Key * x.Value);
        if (Math.Abs(banknotesTotal - amount.Amount) > 0.01m)
        {
            _logger.LogWarning(
                "Deposit: сумма купюр {BanknotesTotal} не совпадает с заявленной {Amount}, счёт {AccountId}",
                banknotesTotal, amount, accountId);

            return Error.Validation(
                $"Сумма купюр ({banknotesTotal:C}) не совпадает с заявленной ({amount:C})");
        }

        var account = await _accounts.GetByIdAsync(accountId, ct);
        if (account is null)
        {
            _logger.LogWarning("Deposit: счёт {AccountId} не найден", accountId);
            return Error.NotFound("Счет не найден");
        }

        var atm = await _atms.GetByIdAsync(atmId, ct);
        if (atm is null)
        {
            _logger.LogWarning(
                "Deposit: банкомат {AtmId} не найден, счёт {AccountId}", atmId, accountId);
            return Error.NotFound("Банкомат не найден");
        }

        if (!atm.IsActive)
        {
            _logger.LogWarning(
                "Deposit: банкомат {AtmId} неактивен, счёт {AccountId}", atmId, accountId);
            return Error.Business("Банкомат неактивен");
        }

        var depositResult = atm.DepositBanknotes(banknotes);
        if (depositResult.IsFailure)
        {
            _logger.LogWarning(
                "Deposit: не удалось принять купюры, банкомат {AtmId}, причина: {Reason}",
                atmId, depositResult.Error.Message);
            return depositResult.Error;
        }

        var creditResult = account.Credit(amount);
        if (creditResult.IsFailure)
        {
            _logger.LogWarning(
                "Deposit: зачисление отклонено, счёт {AccountId}, причина: {Reason}",
                accountId, creditResult.Error.Message);
            return creditResult.Error;
        }

        var log = Transaction.Record(
            account.Id, atm.Id, TransactionType.Deposit,
            amount, account.Balance - amount, account.Balance,
            $"Внесение {amount}");

        await _transactions.AddAsync(log, ct);
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Deposit: успешно, счёт {AccountId}, банкомат {AtmId}, сумма {Amount}, новый баланс {Balance}",
            accountId, atmId, amount, account.Balance);

        return account.Balance;
    }

    private static string FormatBanknotes(IReadOnlyDictionary<int, int> banknotes)
    {
        if (banknotes.Count == 0)
            return "[]";

        return "[" + string.Join(", ", banknotes
            .Where(x => x.Value > 0)
            .OrderByDescending(x => x.Key)
            .Select(x => $"{x.Key}×{x.Value}")) + "]";
    }
}