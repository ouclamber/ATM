using Bankomat.Application.Abstractions;
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
            fromAccountId, atmId, amountValue, MaskCard(toCardRaw));

        var amountResult = Money.Create(amountValue);
        if (amountResult.IsFailure)
        {
            _logger.LogWarning(
                "Transfer: невалидная сумма {Amount} — {Reason}",
                amountValue, amountResult.Error.Message);
            return amountResult.Error;
        }
        var amount = amountResult.Value;

        if (amount.Amount > _config.MaxTransferPerTransaction)
        {
            _logger.LogWarning(
                "Transfer: превышен лимит перевода, счёт {FromAccountId}, сумма {Amount}, лимит {Limit}",
                fromAccountId, amount, _config.MaxTransferPerTransaction);

            return Error.Business(
                $"Лимит перевода: {_config.MaxTransferPerTransaction:C}");
        }

        var toCardResult = CardNumber.Create(toCardRaw);
        if (toCardResult.IsFailure)
        {
            _logger.LogWarning(
                "Transfer: неверный номер карты получателя, счёт {FromAccountId}",
                fromAccountId);
            return toCardResult.Error;
        }
        var toCard = toCardResult.Value;

        var from = await _accounts.GetByIdAsync(fromAccountId, ct);
        if (from is null)
        {
            _logger.LogWarning(
                "Transfer: счёт отправителя {FromAccountId} не найден", fromAccountId);
            return Error.NotFound("Счет отправителя не найден");
        }

        if (from.CardNumber == toCard)
        {
            _logger.LogWarning(
                "Transfer: попытка перевода на свою же карту, счёт {FromAccountId}",
                fromAccountId);
            return Error.Validation("Нельзя перевести на свою же карту");
        }

        var to = await _accounts.GetByCardNumberAsync(toCard, ct);
        if (to is null)
        {
            _logger.LogWarning(
                "Transfer: счёт получателя по карте {ToCard} не найден, счёт {FromAccountId}",
                MaskCard(toCard.Value), fromAccountId);
            return Error.NotFound("Счет получателя не найден по номеру карты");
        }

        await _uow.BeginTransactionAsync(ct);
        try
        {
            var debitResult = from.Debit(amount);
            if (debitResult.IsFailure)
            {
                await _uow.RollbackAsync(ct);
                _logger.LogWarning(
                    "Transfer: списание отклонено, счёт {FromAccountId}, сумма {Amount}, баланс {Balance}, причина: {Reason}",
                    fromAccountId, amount, from.Balance, debitResult.Error.Message);
                return debitResult.Error;
            }

            var creditResult = to.Credit(amount);
            if (creditResult.IsFailure)
            {
                await _uow.RollbackAsync(ct);
                _logger.LogError(
                    "Transfer: зачисление не удалось, счёт {ToAccountId}, причина: {Reason}",
                    to.Id, creditResult.Error.Message);
                return creditResult.Error;
            }

            var outLog = Transaction.Record(
                from.Id, atmId, TransactionType.TransferOut,
                amount, from.Balance + amount, from.Balance,
                $"Перевод на {to.CardNumber.Formatted()}", to.CardNumber.Value);

            var inLog = Transaction.Record(
                to.Id, atmId, TransactionType.TransferIn,
                amount, to.Balance - amount, to.Balance,
                $"Перевод от {from.CardNumber.Formatted()}", from.CardNumber.Value);

            await _transactions.AddAsync(outLog, ct);
            await _transactions.AddAsync(inLog, ct);
            await _uow.SaveChangesAsync(ct);
            await _uow.CommitAsync(ct);

            _logger.LogInformation(
                "Transfer: успешно, счёт {FromAccountId} → счёт {ToAccountId}, сумма {Amount}, новый баланс отправителя {Balance}",
                fromAccountId, to.Id, amount, from.Balance);

            return from.Balance;
        }
        catch (Exception ex)
        {
            await _uow.RollbackAsync(ct);
            _logger.LogError(ex,
                "Transfer: критическая ошибка, счёт {FromAccountId} → {ToAccountId}, сумма {Amount}",
                fromAccountId, to.Id, amount);
            throw;
        }
    }

    private static string MaskCard(string card)
    {
        var clean = card.Replace(" ", "").Replace("-", "");
        return clean.Length >= 4 ? $"**** **** **** {clean[^4..]}" : "****";
    }
}