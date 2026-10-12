using Bankomat.Application.Abstractions;
using Bankomat.Domain.Accounts;
using Bankomat.Domain.Common;
using Bankomat.Domain.Transactions;
using Bankomat.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Bankomat.Application.Authentication;

public class LoginHandler
{
    private readonly IBankAccountRepository _accounts;
    private readonly ITransactionRepository _transactions;
    private readonly IPinHasher _hasher;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<LoginHandler> _logger;

    public LoginHandler(
        IBankAccountRepository accounts,
        ITransactionRepository transactions,
        IPinHasher hasher,
        IUnitOfWork uow,
        ILogger<LoginHandler> logger)
    {
        _accounts = accounts;
        _transactions = transactions;
        _hasher = hasher;
        _uow = uow;
        _logger = logger;
    }

    public async Task<Result<BankAccount>> HandleAsync(
        string cardNumberRaw, string pinRaw, int atmId,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Login: попытка входа, банкомат {AtmId}", atmId);

        var cardResult = CardNumber.Create(cardNumberRaw);
        if (cardResult.IsFailure)
        {
            _logger.LogWarning("Login: неверный формат карты");
            return cardResult.Error;
        }
        var card = cardResult.Value;

        var pinResult = PinCode.Create(pinRaw);
        if (pinResult.IsFailure)
        {
            _logger.LogWarning(
                "Login: неверный формат PIN, карта {Card}", MaskCard(card.Value));
            return pinResult.Error;
        }

        var account = await _accounts.GetByCardNumberAsync(card, ct);
        if (account is null)
        {
            _logger.LogWarning("Login: карта {Card} не найдена", MaskCard(card.Value));
            return Error.NotFound("Карта не найдена");
        }

        if (account.IsBlocked)
        {
            _logger.LogWarning(
                "Login: карта {Card} заблокирована, счёт {AccountId}",
                MaskCard(card.Value), account.Id);
            return Error.Forbidden("Карта заблокирована. Обратитесь в банк.");
        }

        if (!_hasher.Verify(pinResult.Value.Value, account.PinHash))
        {
            account.RegisterFailedLogin();
            await _uow.SaveChangesAsync(ct);

            _logger.LogWarning(
                "Login: неверный PIN, карта {Card}, счёт {AccountId}, попыток {Attempts}, заблокирована: {Blocked}",
                MaskCard(card.Value), account.Id, account.FailedLoginAttempts, account.IsBlocked);

            return account.IsBlocked
                ? Error.Forbidden("Карта заблокирована из-за 3 неудачных попыток ввода PIN")
                : Error.Forbidden($"Неверный PIN. Осталось попыток: {3 - account.FailedLoginAttempts}");
        }

        account.RegisterSuccessfulLogin();

        var log = Transaction.Record(
            account.Id, atmId, TransactionType.Login,
            Money.Zero, account.Balance, account.Balance,
            "Вход в систему");

        await _transactions.AddAsync(log, ct);
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Login: успешный вход, карта {Card}, счёт {AccountId}, баланс {Balance}",
            MaskCard(card.Value), account.Id, account.Balance);

        return account;
    }

    private static string MaskCard(string card) =>
        card.Length >= 4 ? $"**** **** **** {card[^4..]}" : "****";
}