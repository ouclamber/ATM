using Bankomat.Domain.Accounts;
using Bankomat.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Bankomat.Tests.Domain;

public class BankAccountTests
{
    // ==================== ФАБРИКА ДЛЯ ТЕСТОВ ====================

    private static BankAccount CreateAccount(decimal balance = 1000m) =>
        BankAccount.Create(
            CardNumber.Create("1111222233334444").Value,
            "hash",
            "Иван Иванов",
            Money.Of(balance)).Value;

    // ==================== СОЗДАНИЕ ====================

    [Fact]
    public void Create_ValidData_Succeeds()
    {
        var result = BankAccount.Create(
            CardNumber.Create("1111222233334444").Value,
            "hash",
            "Test",
            Money.Of(1000));

        result.IsSuccess.Should().BeTrue();
        result.Value.Balance.Amount.Should().Be(1000);
        result.Value.IsBlocked.Should().BeFalse();
        result.Value.FailedLoginAttempts.Should().Be(0);
        result.Value.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Create_EmptyHolderName_Fails()
    {
        var result = BankAccount.Create(
            CardNumber.Create("1111222233334444").Value,
            "hash",
            "",
            Money.Of(1000));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VALIDATION");
    }

    [Fact]
    public void Create_WhitespaceHolderName_Fails()
    {
        var result = BankAccount.Create(
            CardNumber.Create("1111222233334444").Value,
            "hash",
            "   ",
            Money.Of(1000));

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Create_EmptyPinHash_Fails()
    {
        var result = BankAccount.Create(
            CardNumber.Create("1111222233334444").Value,
            "",
            "Test",
            Money.Of(1000));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VALIDATION");
    }

    // ==================== DEBIT ====================

    [Fact]
    public void Debit_ValidAmount_DecreasesBalance()
    {
        var acc = CreateAccount(1000);

        var result = acc.Debit(Money.Of(300));

        result.IsSuccess.Should().BeTrue();
        acc.Balance.Amount.Should().Be(700);
    }

    [Fact]
    public void Debit_ExactBalance_Succeeds()
    {
        var acc = CreateAccount(1000);

        var result = acc.Debit(Money.Of(1000));

        result.IsSuccess.Should().BeTrue();
        acc.Balance.Amount.Should().Be(0);
    }

    [Fact]
    public void Debit_MoreThanBalance_Fails()
    {
        var acc = CreateAccount(1000);

        var result = acc.Debit(Money.Of(1001));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BUSINESS");
        acc.Balance.Amount.Should().Be(1000);   // баланс не изменился
    }

    [Fact]
    public void Debit_Zero_Fails()
    {
        var acc = CreateAccount(1000);

        var result = acc.Debit(Money.Of(0));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VALIDATION");
    }

    [Fact]
    public void Debit_OnBlockedCard_Fails()
    {
        var acc = CreateAccount(1000);
        acc.RegisterFailedLogin();
        acc.RegisterFailedLogin();
        acc.RegisterFailedLogin();

        acc.IsBlocked.Should().BeTrue();
        var result = acc.Debit(Money.Of(100));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("FORBIDDEN");
    }

    // ==================== CREDIT ====================

    [Fact]
    public void Credit_ValidAmount_IncreasesBalance()
    {
        var acc = CreateAccount(1000);

        var result = acc.Credit(Money.Of(500));

        result.IsSuccess.Should().BeTrue();
        acc.Balance.Amount.Should().Be(1500);
    }

    [Fact]
    public void Credit_Zero_Fails()
    {
        var acc = CreateAccount(1000);

        var result = acc.Credit(Money.Of(0));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VALIDATION");
    }

    [Fact]
    public void Credit_OnBlockedCard_Succeeds()
    {
        // ВАЖНО: зачисление на заблокированную карту разрешено (например, при переводе).
        // Это соответствует реальной банковской практике.
        var acc = CreateAccount(1000);
        acc.RegisterFailedLogin();
        acc.RegisterFailedLogin();
        acc.RegisterFailedLogin();

        var result = acc.Credit(Money.Of(500));

        result.IsSuccess.Should().BeTrue();
        acc.Balance.Amount.Should().Be(1500);
    }

    [Fact]
    public void Credit_OnZeroBalance_Succeeds()
    {
        var acc = CreateAccount(0);

        var result = acc.Credit(Money.Of(100));

        result.IsSuccess.Should().BeTrue();
        acc.Balance.Amount.Should().Be(100);
    }

    // ==================== FAILED LOGIN ====================

    [Fact]
    public void RegisterFailedLogin_IncrementsAttempts()
    {
        var acc = CreateAccount();

        acc.RegisterFailedLogin();

        acc.FailedLoginAttempts.Should().Be(1);
        acc.IsBlocked.Should().BeFalse();
    }

    [Fact]
    public void RegisterFailedLogin_TwoTimes_NotBlockedYet()
    {
        var acc = CreateAccount();

        acc.RegisterFailedLogin();
        acc.RegisterFailedLogin();

        acc.FailedLoginAttempts.Should().Be(2);
        acc.IsBlocked.Should().BeFalse();
    }

    [Fact]
    public void RegisterFailedLogin_ThreeTimes_BlocksCard()
    {
        var acc = CreateAccount();

        acc.RegisterFailedLogin();   // 1
        acc.RegisterFailedLogin();   // 2
        var third = acc.RegisterFailedLogin();  // 3 → блок

        third.IsFailure.Should().BeTrue();
        acc.IsBlocked.Should().BeTrue();
        acc.FailedLoginAttempts.Should().Be(3);
    }

    [Fact]
    public void RegisterFailedLogin_OnBlockedCard_Fails()
    {
        var acc = CreateAccount();
        acc.RegisterFailedLogin();
        acc.RegisterFailedLogin();
        acc.RegisterFailedLogin();

        var result = acc.RegisterFailedLogin();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("FORBIDDEN");
    }

    // ==================== SUCCESSFUL LOGIN ====================

    [Fact]
    public void RegisterSuccessfulLogin_ResetsAttempts()
    {
        var acc = CreateAccount();
        acc.RegisterFailedLogin();
        acc.RegisterFailedLogin();

        acc.RegisterSuccessfulLogin();

        acc.FailedLoginAttempts.Should().Be(0);
        acc.LastAccessAt.Should().NotBeNull();
        acc.LastAccessAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void RegisterSuccessfulLogin_WithoutPriorFailures_Succeeds()
    {
        var acc = CreateAccount();

        acc.RegisterSuccessfulLogin();

        acc.FailedLoginAttempts.Should().Be(0);
        acc.LastAccessAt.Should().NotBeNull();
    }

    // ==================== PIN ====================

    [Fact]
    public void ChangePinHash_ValidHash_Updates()
    {
        var acc = CreateAccount();

        var result = acc.ChangePinHash("newHash");

        result.IsSuccess.Should().BeTrue();
        acc.PinHash.Should().Be("newHash");
    }

    [Fact]
    public void ChangePinHash_Empty_Fails()
    {
        var acc = CreateAccount();

        var result = acc.ChangePinHash("");

        result.IsFailure.Should().BeTrue();
        acc.PinHash.Should().Be("hash");
    }

    [Fact]
    public void ChangePinHash_Whitespace_Fails()
    {
        var acc = CreateAccount();

        var result = acc.ChangePinHash("   ");

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void ChangePinHash_Null_Fails()
    {
        var acc = CreateAccount();

        var result = acc.ChangePinHash(null!);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void ChangePinHash_AfterBlock_Succeeds()
    {
        // Смена PIN доступна даже на заблокированной карте (после разблокировки)
        var acc = CreateAccount();
        acc.RegisterFailedLogin();
        acc.RegisterFailedLogin();
        acc.RegisterFailedLogin();

        var result = acc.ChangePinHash("newHash");

        result.IsSuccess.Should().BeTrue();
        acc.PinHash.Should().Be("newHash");
    }
}