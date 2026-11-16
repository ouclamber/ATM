using Bankomat.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Bankomat.Infrastructure.Configuration;

public class AdminAuthService : IAdminAuthService
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    private readonly string _configuredUsername;
    private readonly string _configuredPasswordHash;
    private readonly TimeProvider _time;
    private readonly object _sync = new();

    private int _failedAttempts;
    private DateTimeOffset? _lockedUntil;

    public AdminAuthService(IConfiguration configuration, TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;

        _configuredUsername = configuration.GetValue<string>("Admin:Username") ?? "admin";

        _configuredPasswordHash = configuration.GetValue<string>("Admin:PasswordHash")
            ?? throw new InvalidOperationException(
                "Admin:PasswordHash не настроен (appsettings.json или переменная окружения Admin__PasswordHash)");
    }

    public TimeSpan? LockoutRemaining
    {
        get
        {
            lock (_sync)
            {
                if (_lockedUntil is null) return null;
                var left = _lockedUntil.Value - _time.GetUtcNow();
                return left > TimeSpan.Zero ? left : null;
            }
        }
    }

    public bool Verify(string username, string password)
    {
        lock (_sync)
        {
            if (_lockedUntil is not null)
            {
                if (_time.GetUtcNow() < _lockedUntil.Value)
                    return false;

                _lockedUntil = null;
                _failedAttempts = 0;
            }
        }

        var ok = Check(username, password);

        lock (_sync)
        {
            if (ok)
            {
                _failedAttempts = 0;
            }
            else if (++_failedAttempts >= MaxFailedAttempts)
            {
                _lockedUntil = _time.GetUtcNow() + LockoutDuration;
            }
        }

        return ok;
    }

    private bool Check(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return false;

        // BCrypt проверяем всегда, даже при неверном логине: время ответа не выдаёт, угадан ли логин.
        bool passwordOk;
        try
        {
            passwordOk = BCrypt.Net.BCrypt.Verify(password, _configuredPasswordHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            throw new InvalidOperationException("Admin:PasswordHash не является корректным BCrypt-хешем");
        }

        var userOk = string.Equals(username, _configuredUsername, StringComparison.OrdinalIgnoreCase);
        return userOk && passwordOk;
    }
}
