namespace Bankomat.Application.Abstractions;

public interface IAdminAuthService
{
    /// <summary>Проверяет логин и пароль. Во время блокировки всегда возвращает false.</summary>
    bool Verify(string username, string password);

    /// <summary>Сколько ещё длится блокировка после серии неудачных попыток (null — не заблокирован).</summary>
    TimeSpan? LockoutRemaining { get; }
}
