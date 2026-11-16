using Bankomat.Application.Abstractions;
using Bankomat.Application.Common;
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
            atmId, LogFormat.Banknotes(banknotes));

        var atm = await _atms.GetByIdAsync(atmId, ct);
        if (atm is null)
        {
            _logger.LogWarning("Refill: банкомат {AtmId} не найден", atmId);
            return Error.NotFound("Банкомат не найден");
        }

        var cashBefore = atm.TotalCash().Amount;

        var result = atm.Refill(banknotes);
        if (result.IsFailure)
        {
            _logger.LogWarning(
                "Refill: пополнение отклонено, банкомат {AtmId}, причина: {Reason}",
                atmId, result.Error.Message);
            return result.Error;
        }

        var saved = await _uow.TrySaveChangesAsync(ct);
        if (saved.IsFailure)
            return saved.Error;

        var cashAfter = atm.TotalCash().Amount;
        _logger.LogInformation(
            "Refill: успешно, банкомат {AtmId}, добавлено {Added}, всего в банкомате {Total}",
            atmId, cashAfter - cashBefore, cashAfter);

        return Result.Success();
    }
}
