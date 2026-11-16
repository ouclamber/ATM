namespace Bankomat.Application.Abstractions;

/// <summary>
/// Оптимистическая блокировка не прошла: строка изменилась между чтением и записью.
/// Инфраструктура переводит в это исключение свою специфичную ошибку (DbUpdateConcurrencyException),
/// чтобы слой Application не зависел от EF Core.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message, Exception? inner = null)
        : base(message, inner) { }
}
