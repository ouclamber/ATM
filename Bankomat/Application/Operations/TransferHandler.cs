using Bankomat.Application.Abstractions;
using Bankomat.Application.Common;
using Bankomat.Domain.Accounts;
using Bankomat.Domain.Common;
using Bankomat.Domain.Transactions;
using Bankomat.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Bankomat.Application.Operations;

public class TransferHandler
{
    private readonly IBankAccountRepository _accounts;
    private readonly ITransactionRepository _transactions;
    private readonly IBankConfig _config;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<TransferHandler> _logger;

    public TransferHandler(
        IBankAccountRepository accounts,
        ITransactionRepository transactions,
        IBankConfig config,
        IUnitOfWork uow,
        ILogger<TransferHandler> logger)
    {
        _accounts = accounts;
        _transactions = transactions;
        _config = config;
        _uow = uow;
        _logger = logger;
    }

    public async Task<Result<Money>> HandleAsync(
        int fromAccountId, int atmId, string toCardRaw, decimal amountValue,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Transfer: начало операции, счёт {FromAccountId}, банкомат {AtmId}, сумма {Amount}, карта получателя {ToCard}",
            fromAccountId, atmId, amountValue, LogFormat.MaskCard(toCardRaw));

        var amountResult = Money.Create(amountValue);
        if (amountResult.IsFailure)
            return Reject(fromAccountId, amountResult.Error);
        var amount = amountResult.Value;

        if (amount <= Money.Zero)
            return Reject(fromAccountId, Error.Validation("Сумма должна быть больше нуля"));

        if (amount.Amount > _config.MaxTransferPerTransaction)
            return Reject(fromAccountId, Error.Business(
                $"Лимит перевода: {_config.MaxTransferPerTransaction:C}"));

        var toCardResult = CardNumber.Create(toCardRaw);
        if (toCardResult.IsFailure)
            return Reject(fromAccountId, toCardResult.Error);
        var toCard = toCardResult.Value;

        var from = await _accounts.GetByIdAsync(fromAccountId, ct);
        if (from is null)
            return Reject(fromAccountId, Error.NotFound("Счет отправителя не найден"));

        if (from.CardNumber == toCard)
            return Reject(fromAccountId, Error.Validation("Нельзя перевести на свою же карту"));

        var to = await _accounts.GetByCardNumberAsync(toCard, ct);
        if (to is null)
            return Reject(fromAccountId, Error.NotFound("Счет получателя не найден по номеру карты"));

        var fromBefore = from.Balance;
        var toBefore = to.Balance;

        var debitResult = from.Debit(amount);
        if (debitResult.IsFailure)
            return Reject(fromAccountId, debitResult.Error);

        var creditResult = to.Credit(amount);
        if (creditResult.IsFailure)
            return Reject(fromAccountId, creditResult.Error);

        await _transactions.AddAsync(Transaction.Record(
            from.Id, atmId, TransactionType.TransferOut,
            amount, fromBefore, from.Balance,
            $"Перевод на {to.CardNumber.Formatted()}", to.CardNumber.Value), ct);

        await _transactions.AddAsync(Transaction.Record(
            to.Id, atmId, TransactionType.TransferIn,
            amount, toBefore, to.Balance,
            $"Перевод от {from.CardNumber.Formatted()}", from.CardNumber.Value), ct);

        // Оба счёта и обе записи журнала уходят в БД одним SaveChanges — это уже одна транзакция БД.
        // Явная BeginTransaction/Commit вокруг одного SaveChanges ничего не добавляла.
        var saved = await _uow.TrySaveChangesAsync(ct);
        if (saved.IsFailure)
            return Reject(fromAccountId, saved.Error);

        _logger.LogInformation(
            "Transfer: успешно, счёт {FromAccountId} → счёт {ToAccountId}, сумма {Amount}",
            fromAccountId, to.Id, amount);

        return from.Balance;
    }

    private Error Reject(int accountId, Error error)
    {
        _logger.LogWarning(
            "Transfer: отклонено, счёт {AccountId}, код {Code}, причина: {Reason}",
            accountId, error.Code, error.Message);
        return error;
    }
}
