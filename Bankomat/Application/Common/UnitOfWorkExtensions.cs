using Bankomat.Application.Abstractions;
using Bankomat.Domain.Common;

namespace Bankomat.Application.Common;

internal static class UnitOfWorkExtensions
{
    public static readonly Error ConcurrencyError =
        Error.Conflict("Данные счёта или банкомата изменились во время операции. Повторите попытку.");

    /// <summary>Сохраняет изменения и превращает конфликт параллельного доступа в бизнес-ошибку.</summary>
    public static async Task<Result> TrySaveChangesAsync(this IUnitOfWork uow, CancellationToken ct)
    {
        try
        {
            await uow.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (ConcurrencyConflictException)
        {
            return ConcurrencyError;
        }
    }
}
