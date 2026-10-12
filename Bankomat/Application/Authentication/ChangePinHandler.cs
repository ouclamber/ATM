using Bankomat.Application.Abstractions;
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
        _logger.LogInformation(
            "ChangePin: запрос смены PIN, счёт {AccountId}", accountId);

        var oldPinResult = PinCode.Create(oldPinRaw);
        if (oldPinResult.IsFailure)
        {
            _logger.LogWarning(
                "ChangePin: неверный формат старого PIN, счёт {AccountId}", accountId);
            return oldPinResult.Error;
        }

        var newPinResult = PinCode.Create(newPinRaw);
        if (newPinResult.IsFailure)
        {
            _logger.LogWarning(
                "ChangePin: неверный формат нового PIN, счёт {AccountId}", accountId);
            return newPinResult.Error;
        }

        if (oldPinRaw == newPinRaw)
        {
            _logger.LogWarning(
                "ChangePin: новый PIN совпадает со старым, счёт {AccountId}", accountId);
            return Error.Validation("Новый PIN совпадает со старым");
        }

        var account = await _accounts.GetByIdAsync(accountId, ct);
        if (account is null)
        {
            _logger.LogWarning("ChangePin: счёт {AccountId} не найден", accountId);
            return Error.NotFound("Счет не найден");
        }

        if (!_hasher.Verify(oldPinResult.Value.Value, account.PinHash))
        {
            _logger.LogWarning(
                "ChangePin: неверный текущий PIN, счёт {AccountId}", accountId);
            return Error.Forbidden("Неверный текущий PIN");
        }

        var hash = _hasher.Hash(newPinResult.Value.Value);
        var result = account.ChangePinHash(hash);
        if (result.IsFailure)
        {
            _logger.LogWarning(
                "ChangePin: не удалось изменить PIN, счёт {AccountId}, причина: {Reason}",
                accountId, result.Error.Message);
            return result.Error;
        }

        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation(
            "ChangePin: успех, счёт {AccountId}", accountId);

        return Result.Success();
    }
}