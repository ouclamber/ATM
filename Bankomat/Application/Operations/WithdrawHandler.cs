using Bankomat.Application.Abstractions;
using Bankomat.Application.Common;
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

        // 1. Дешёвые проверки без обращения к БД
        var amountResult = Money.Create(amountValue);
        if (amountResult.IsFailure)
            return Reject(accountId, amountResult.Error);
        var amount = amountResult.Value;

        if (amount <= Money.Zero)
            return Reject(accountId, Error.Validation("Сумма должна быть больше нуля"));

        if (amount.Amount > _config.MaxWithdrawalPerTransaction)
            return Reject(accountId, Error.Business(
                $"Лимит за операцию: {_config.MaxWithdrawalPerTransaction:C}"));

        if (!amount.IsMultipleOf(100))
            return Reject(accountId, Error.Validation("Сумма должна быть кратна 100"));

        // 2. Загрузка агрегатов
        var account = await _accounts.GetByIdAsync(accountId, ct);
        if (account is null)
            return Reject(accountId, Error.NotFound("Счет не найден"));

        var atm = await _atms.GetByIdAsync(atmId, ct);
        if (atm is null)
            return Reject(accountId, Error.NotFound("Банкомат не найден"));

        if (!atm.IsActive)
            return Reject(accountId, Error.Business("Банкомат неактивен"));

        // 3. Дневной лимит
        var last24 = await _transactions.GetWithdrawSumLast24hAsync(accountId, ct);
        if (last24 + amount.Amount > _config.MaxWithdrawalPerDay)
            return Reject(accountId, Error.Business(
                $"Превышен дневной лимит ({_config.MaxWithdrawalPerDay:C}). Уже снято за 24 ч: {last24:C}"));

        // 4. Доменная логика: план выдачи → списание со счёта → списание из кассет
        var planResult = atm.PlanWithdrawal(amount);
        if (planResult.IsFailure)
            return Reject(accountId, planResult.Error);
        var plan = planResult.Value;

        var balanceBefore = account.Balance;

        var debitResult = account.Debit(amount);
        if (debitResult.IsFailure)
            return Reject(accountId, debitResult.Error);

        var applyResult = atm.ApplyWithdrawal(plan);
        if (applyResult.IsFailure)
        {
            // Не должно случаться: план построен по этим же кассетам
            _logger.LogError(
                "Withdraw: ошибка применения выдачи, счёт {AccountId}, банкомат {AtmId}, причина: {Reason}",
                accountId, atmId, applyResult.Error.Message);
            return applyResult.Error;
        }

        await _transactions.AddAsync(Transaction.Record(
            account.Id, atm.Id, TransactionType.Withdraw,
            amount, balanceBefore, account.Balance,
            $"Снятие {amount}"), ct);

        // 5. Баланс, кассеты и журнал сохраняются одним атомарным SaveChanges.
        //    Токены конкурентности (xmin) не дадут двум банкоматам одновременно списать одни и те же деньги.
        var saved = await _uow.TrySaveChangesAsync(ct);
        if (saved.IsFailure)
            return Reject(accountId, saved.Error);

        _logger.LogInformation(
            "Withdraw: успешно, счёт {AccountId}, банкомат {AtmId}, сумма {Amount}, выдано купюр: {Banknotes}",
            accountId, atmId, amount, LogFormat.Banknotes(plan.Banknotes));

        return new WithdrawResponse(account.Balance, plan.Banknotes);
    }

    private Error Reject(int accountId, Error error)
    {
        _logger.LogWarning(
            "Withdraw: отклонено, счёт {AccountId}, код {Code}, причина: {Reason}",
            accountId, error.Code, error.Message);
        return error;
    }
}
