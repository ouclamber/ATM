using Bankomat.Infrastructure.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Bankomat.Tests.Infrastructure;

public class AdminAuthServiceTests
{
    private static IConfiguration BuildConfig(string? username, string? hash)
    {
        var dict = new Dictionary<string, string?>
        {
            ["Admin:Username"] = username,
            ["Admin:PasswordHash"] = hash
        };
        return new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();
    }

    [Fact]
    public void Verify_CorrectCredentials_ReturnsTrue()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("admin123");
        var service = new AdminAuthService(BuildConfig("admin", hash));

        service.Verify("admin", "admin123").Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("admin123");
        var service = new AdminAuthService(BuildConfig("admin", hash));

        service.Verify("admin", "wrong").Should().BeFalse();
    }

    [Fact]
    public void Verify_WrongUsername_ReturnsFalse()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("admin123");
        var service = new AdminAuthService(BuildConfig("admin", hash));

        service.Verify("hacker", "admin123").Should().BeFalse();
    }

    [Fact]
    public void Verify_CaseInsensitiveUsername()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("admin123");
        var service = new AdminAuthService(BuildConfig("admin", hash));

        service.Verify("ADMIN", "admin123").Should().BeTrue();
    }

    [Theory]
    [InlineData("", "admin123")]
    [InlineData("admin", "")]
    [InlineData("", "")]
    public void Verify_EmptyInput_ReturnsFalse(string username, string password)
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("admin123");
        var service = new AdminAuthService(BuildConfig("admin", hash));

        service.Verify(username, password).Should().BeFalse();
    }

    [Fact]
    public void Constructor_MissingHash_Throws()
    {
        var act = () => new AdminAuthService(BuildConfig("admin", null));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*PasswordHash*");
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void Verify_TooManyFailures_LocksOutEvenCorrectPassword()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("admin123", workFactor: 4);
        var time = new ManualTimeProvider();
        var service = new AdminAuthService(BuildConfig("admin", hash), time);

        for (var i = 0; i < AdminAuthService.MaxFailedAttempts; i++)
            service.Verify("admin", "wrong").Should().BeFalse();

        service.LockoutRemaining.Should().NotBeNull();
        service.Verify("admin", "admin123").Should().BeFalse("во время блокировки вход запрещён");
    }

    [Fact]
    public void Verify_AfterLockoutExpires_AllowsLogin()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("admin123", workFactor: 4);
        var time = new ManualTimeProvider();
        var service = new AdminAuthService(BuildConfig("admin", hash), time);

        for (var i = 0; i < AdminAuthService.MaxFailedAttempts; i++)
            service.Verify("admin", "wrong");

        time.Now += AdminAuthService.LockoutDuration + TimeSpan.FromSeconds(1);

        service.LockoutRemaining.Should().BeNull();
        service.Verify("admin", "admin123").Should().BeTrue();
    }

    [Fact]
    public void Verify_SuccessResetsFailureCounter()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("admin123", workFactor: 4);
        var service = new AdminAuthService(BuildConfig("admin", hash), new ManualTimeProvider());

        for (var i = 0; i < AdminAuthService.MaxFailedAttempts - 1; i++)
            service.Verify("admin", "wrong");
        service.Verify("admin", "admin123").Should().BeTrue();
        service.Verify("admin", "wrong");

        service.LockoutRemaining.Should().BeNull();
    }

    [Fact]
    public void Verify_InvalidHashInConfig_ThrowsClearError()
    {
        var service = new AdminAuthService(BuildConfig("admin", "not-a-bcrypt-hash"));

        var act = () => service.Verify("admin", "x");

        act.Should().Throw<InvalidOperationException>().WithMessage("*BCrypt*");
    }
}
