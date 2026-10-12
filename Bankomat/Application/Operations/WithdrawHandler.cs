using Bankomat.Application.Abstractions;
using Bankomat.Domain.Accounts;
using Bankomat.Domain.Atms;
using Bankomat.Domain.Common;
using Bankomat.Domain.Transactions;
using Bankomat.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Bankomat.Application.Operations;

public record WithdrawResponse(Money NewBalance, IReadOnlyDictionary<int, int> Banknotes);

public class WithdrawHandler
{
    private readonly IBankAccountRepository _accounts;
    private readonly IAtmRepository _atms;
    private readonly ITransactionRepository _transactions;
    private readonly IBankConfig _config;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<WithdrawHandler> _logger;

    public WithdrawHandler(
        IBankAccountRepository accounts,
        IAtmRepository atms,
        ITransactionRepository transactions,
        IBankConfig config,
        IUnitOfWork uow,
        ILogger<WithdrawHandler> logger)
    {
        _accounts = accounts;
        _atms = atms;
        _transactions = transactions;
        _config = config;
        _uow = uow;
        _logger = logger;
    }

    public async Task<Result<WithdrawResponse>> HandleAsync(
        int accountId, int atmId, decimal amountValue, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Withdraw: начало операции, счёт {AccountId}, банкомат {AtmId}, сумма {Amount}",
            accountId, atmId, amountValue);

        var amountResult = Money.Create(amountValue);
        if (amountResult.IsFailure)
        {
            _logger.LogWarning(
                "Withdraw: невалидная сумма {Amount} — {Reason}",
                amountValue, amountResult.Error.Message);
            return amountResult.Error;
        }
        var amount = amountResult.Value;

        if (amount.Amount > _config.MaxWithdrawalPerTransaction)
        {
            _logger.LogWarning(
                "Withdraw: превышен лимит за операцию, счёт {AccountId}, сумма {Amount}, лимит {Limit}",
                accountId, amount, _config.MaxWithdrawalPerTransaction);

            return Error.Business(
                $"Лимит за операцию: {_config.MaxWithdrawalPerTransaction:C}");
        }

        if (!amount.IsMultipleOf(100))
        {
            _logger.LogWarning(
                "Withdraw: сумма {Amount} не кратна 100, счёт {AccountId}",
                amount, accountId);

            return Error.Validation("Сумма должна быть кратна 100");
        }

        var account = await _accounts.GetByIdAsync(accountId, ct);
        if (account is null)
        {
            _logger.LogWarning("Withdraw: счёт {AccountId} не найден", accountId);
            return Error.NotFound("Счет не найден");
        }

        var atm = await _atms.GetByIdAsync(atmId, ct);
        if (atm is null)
        {
            _logger.LogWarning(
                "Withdraw: банкомат {AtmId} не найден, счёт {AccountId}",
                atmId, accountId);

            return Error.NotFound("Банкомат не найден");
        }

        if (!atm.IsActive)
        {
            _logger.LogWarning(
                "Withdraw: банкомат {AtmId} неактивен, счёт {AccountId}",
                atmId, accountId);

            return Error.Business("Банкомат неактивен");
        }

        var last24 = await _transactions.GetWithdrawSumLast24hAsync(accountId, ct);
        if (last24 + amount.Amount > _config.MaxWithdrawalPerDay)
        {
            _logger.LogWarning(
                "Withdraw: превышен дневной лимит, счёт {AccountId}, уже снято за 24ч {Last24}, запрос {Amount}, лимит {Limit}",
                accountId, last24, amount, _config.MaxWithdrawalPerDay);

            return Error.Business(
                $"Превышен дневной лимит ({_config.MaxWithdrawalPerDay:C}). Уже снято за 24 ч: {last24:C}");
        }

        var planResult = atm.PlanWithdrawal(amount);
        if (planResult.IsFailure)
        {
            _logger.LogWarning(
                "Withdraw: план выдачи невозможен, счёт {AccountId}, сумма {Amount}, причина: {Reason}",
                accountId, amount, planResult.Error.Message);

            return planResult.Error;
        }
        var plan = planResult.Value;

        var debitResult = account.Debit(amount);
        if (debitResult.IsFailure)
        {
            _logger.LogWarning(
                "Withdraw: списание отклонено, счёт {AccountId}, сумма {Amount}, баланс {Balance}, причина: {Reason}",
                accountId, amount, account.Balance, debitResult.Error.Message);

            return debitResult.Error;
        }

        var applyResult = atm.ApplyWithdrawal(plan);
        if (applyResult.IsFailure)
        {
            _logger.LogError(
                "Withdraw: ошибка применения выдачи, счёт {AccountId}, банкомат {AtmId}, причина: {Reason}",
                accountId, atmId, applyResult.Error.Message);

            return applyResult.Error;
        }

        var log = Transaction.Record(
            account.Id, atm.Id, TransactionType.Withdraw,
            amount, account.Balance + amount, account.Balance,
            $"Снятие {amount}");

        await _transactions.AddAsync(log, ct);
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Withdraw: успешно, счёт {AccountId}, банкомат {AtmId}, сумма {Amount}, новый баланс {Balance}, выдано купюр: {Banknotes}",
            accountId, atmId, amount, account.Balance, FormatBanknotes(plan.Banknotes));

        return new WithdrawResponse(account.Balance, plan.Banknotes);
    }

    private static string FormatBanknotes(IReadOnlyDictionary<int, int> banknotes)
    {
        if (banknotes.Count == 0)
            return "[]";

        return "[" + string.Join(", ", banknotes
            .OrderByDescending(x => x.Key)
            .Select(x => $"{x.Key}×{x.Value}")) + "]";
    }
}