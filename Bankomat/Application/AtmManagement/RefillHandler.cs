using Bankomat.Application.Abstractions;
using Bankomat.Domain.Atms;
using Bankomat.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Bankomat.Application.AtmManagement;

public class RefillHandler
{
    private readonly IAtmRepository _atms;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<RefillHandler> _logger;

    public RefillHandler(
        IAtmRepository atms,
        IUnitOfWork uow,
        ILogger<RefillHandler> logger)
    {
        _atms = atms;
        _uow = uow;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(
        int atmId, IReadOnlyDictionary<int, int> banknotes,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Refill: начало инкассации, банкомат {AtmId}, купюры: {Banknotes}",
            atmId, FormatBanknotes(banknotes));

        if (banknotes.Count == 0)
        {
            _logger.LogWarning(
                "Refill: пустой список купюр, банкомат {AtmId}", atmId);
            return Error.Validation("Не указано ни одной купюры");
        }

        var atm = await _atms.GetByIdAsync(atmId, ct);
        if (atm is null)
        {
            _logger.LogWarning("Refill: банкомат {AtmId} не найден", atmId);
            return Error.NotFound("Банкомат не найден");
        }

        var cashBefore = atm.TotalCash().Amount;

        var result = atm.DepositBanknotes(banknotes);
        if (result.IsFailure)
        {
            _logger.LogWarning(
                "Refill: пополнение отклонено, банкомат {AtmId}, причина: {Reason}",
                atmId, result.Error.Message);
            return result.Error;
        }

        await _uow.SaveChangesAsync(ct);

        var cashAfter = atm.TotalCash().Amount;
        var addedAmount = cashAfter - cashBefore;

        _logger.LogInformation(
            "Refill: успешно, банкомат {AtmId}, добавлено {Added}, всего в банкомате {Total}",
            atmId, addedAmount, cashAfter);

        return Result.Success();
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