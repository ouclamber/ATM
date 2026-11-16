using Bankomat.Application.Abstractions;
using Bankomat.Application.Common;
using Bankomat.Domain.Accounts;
using Bankomat.Domain.Common;
using Bankomat.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Bankomat.Application.Authentication;

public class ChangePinHandler
{
    private readonly IBankAccountRepository _accounts;
    private readonly IPinHasher _hasher;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<ChangePinHandler> _logger;

    public ChangePinHandler(
        IBankAccountRepository accounts,
        IPinHasher hasher,
        IUnitOfWork uow,
        ILogger<ChangePinHandler> logger)
    {
        _accounts = accounts;
        _hasher = hasher;
        _uow = uow;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(
        int accountId, string oldPinRaw, string newPinRaw,
        CancellationToken ct = default)
    {
        _logger.LogInformation("ChangePin: запрос смены PIN, счёт {AccountId}", accountId);

        var oldPinResult = PinCode.Create(oldPinRaw);
        if (oldPinResult.IsFailure)
        {
            _logger.LogWarning("ChangePin: неверный формат старого PIN, счёт {AccountId}", accountId);
            return oldPinResult.Error;
        }

        var newPinResult = PinCode.Create(newPinRaw);
        if (newPinResult.IsFailure)
        {
            _logger.LogWarning("ChangePin: неверный формат нового PIN, счёт {AccountId}", accountId);
            return newPinResult.Error;
        }

        if (oldPinResult.Value == newPinResult.Value)
        {
            _logger.LogWarning("ChangePin: новый PIN совпадает со старым, счёт {AccountId}", accountId);
            return Error.Validation("Новый PIN совпадает со старым");
        }

        var account = await _accounts.GetByIdAsync(accountId, ct);
        if (account is null)
        {
            _logger.LogWarning("ChangePin: счёт {AccountId} не найден", accountId);
            return Error.NotFound("Счет не найден");
        }

        if (account.IsBlocked)
        {
            _logger.LogWarning("ChangePin: карта заблокирована, счёт {AccountId}", accountId);
            return Error.Forbidden("Карта заблокирована. Обратитесь в банк.");
        }

        if (!_hasher.Verify(oldPinResult.Value.Value, account.PinHash))
        {
            // Неверный текущий PIN считается неудачной попыткой входа:
            // иначе через смену PIN можно бесконечно перебирать PIN в открытой сессии.
            account.RegisterFailedLogin();
            var save = await _uow.TrySaveChangesAsync(ct);
            if (save.IsFailure)
                return save.Error;

            _logger.LogWarning(
                "ChangePin: неверный текущий PIN, счёт {AccountId}, попыток {Attempts}, заблокирована: {Blocked}",
                accountId, account.FailedLoginAttempts, account.IsBlocked);

            return account.IsBlocked
                ? Error.Forbidden("Карта заблокирована из-за неверного ввода PIN. Обратитесь в банк.")
                : Error.Forbidden($"Неверный текущий PIN. Осталось попыток: {account.RemainingAttempts}");
        }

        var result = account.ChangePinHash(_hasher.Hash(newPinResult.Value.Value));
        if (result.IsFailure)
        {
            _logger.LogWarning(
                "ChangePin: не удалось изменить PIN, счёт {AccountId}, причина: {Reason}",
                accountId, result.Error.Message);
            return result.Error;
        }

        // Успешная проверка PIN сбрасывает счётчик ошибок, как и при входе
        account.RegisterSuccessfulLogin();

        var saved = await _uow.TrySaveChangesAsync(ct);
        if (saved.IsFailure)
            return saved.Error;

        _logger.LogInformation("ChangePin: успех, счёт {AccountId}", accountId);
        return Result.Success();
    }
}
