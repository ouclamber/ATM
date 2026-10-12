using Bankomat.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Bankomat.Infrastructure.Configuration;

public class AdminAuthService : IAdminAuthService
{
    private readonly string _configuredUsername;
    private readonly string _configuredPasswordHash;

    public AdminAuthService(IConfiguration configuration)
    {
        _configuredUsername = configuration.GetValue<string>("Admin:Username") ?? "admin";

        _configuredPasswordHash = configuration.GetValue<string>("Admin:PasswordHash")
            ?? throw new InvalidOperationException(
                "Admin:PasswordHash не настроен в appsettings.json");
    }

    public bool Verify(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return false;

        if (!string.Equals(username, _configuredUsername, StringComparison.OrdinalIgnoreCase))
            return false;

        return BCrypt.Net.BCrypt.Verify(password, _configuredPasswordHash);
    }
}