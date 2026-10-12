using Bankomat.Application.Abstractions;
using Bankomat.Application.Authentication;
using Bankomat.Domain.Accounts;
using Bankomat.Domain.Transactions;
using Bankomat.Domain.ValueObjects;
using Bankomat.Tests.Application.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Bankomat.Tests.Application;

public class LoginHandlerTests
{
    private readonly Mock<IBankAccountRepository> _accounts = new();
    private readonly Mock<ITransactionRepository> _transactions = new();
    private readonly Mock<IPinHasher> _hasher = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private LoginHandler CreateHandler() =>
        new(
            _accounts.Object,
            _transactions.Object,
            _hasher.Object,
            _uow.Object,
            NullLogger<LoginHandler>.Instance);

    [Fact]
    public async Task Login_ValidCredentials_Succeeds()
    {
        var account = TestData.CreateAccount(id: 1);
        _accounts.Setup(r => r.GetByCardNumberAsync(It.IsAny<CardNumber>(), default))
                 .ReturnsAsync(account);
        _hasher.Setup(h => h.Verify("1234", "hash")).Returns(true);

        var result = await CreateHandler().HandleAsync("1111222233334444", "1234", 1);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(1);
        account.FailedLoginAttempts.Should().Be(0);
    }

    [Fact]
    public async Task Login_WrongPin_IncrementsAttempts()
    {
        var account = TestData.CreateAccount(id: 1);
        _accounts.Setup(r => r.GetByCardNumberAsync(It.IsAny<CardNumber>(), default))
                 .ReturnsAsync(account);
        _hasher.Setup(h => h.Verify("0000", "hash")).Returns(false);

        var result = await CreateHandler().HandleAsync("1111222233334444", "0000", 1);

        result.IsFailure.Should().BeTrue();
        account.FailedLoginAttempts.Should().Be(1);
    }

    [Fact]
    public async Task Login_ThreeWrongPins_BlocksCard()
    {
        var account = TestData.CreateAccount(id: 1);
        _accounts.Setup(r => r.GetByCardNumberAsync(It.IsAny<CardNumber>(), default))
                 .ReturnsAsync(account);
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), "hash")).Returns(false);

        var handler = CreateHandler();
        await handler.HandleAsync("1111222233334444", "0000", 1);
        await handler.HandleAsync("1111222233334444", "0000", 1);
        await handler.HandleAsync("1111222233334444", "0000", 1);

        account.IsBlocked.Should().BeTrue();
    }

    [Fact]
    public async Task Login_OnBlockedCard_Fails()
    {
        var account = TestData.CreateAccount(id: 1, isBlocked: true);
        _accounts.Setup(r => r.GetByCardNumberAsync(It.IsAny<CardNumber>(), default))
                 .ReturnsAsync(account);

        var result = await CreateHandler().HandleAsync("1111222233334444", "1234", 1);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task Login_CardNotFound_Fails()
    {
        _accounts.Setup(r => r.GetByCardNumberAsync(It.IsAny<CardNumber>(), default))
                 .ReturnsAsync((BankAccount?)null);

        var result = await CreateHandler().HandleAsync("9999999999999999", "1234", 1);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Login_InvalidCardFormat_Fails()
    {
        var result = await CreateHandler().HandleAsync("123", "1234", 1);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VALIDATION");
    }

    [Fact]
    public async Task Login_InvalidPinFormat_Fails()
    {
        var result = await CreateHandler().HandleAsync("1111222233334444", "12", 1);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VALIDATION");
    }

    [Fact]
    public async Task Login_Success_ResetsFailedAttempts()
    {
        var account = TestData.CreateAccount(id: 1);
        account.RegisterFailedLogin();
        account.RegisterFailedLogin();

        _accounts.Setup(r => r.GetByCardNumberAsync(It.IsAny<CardNumber>(), default))
                 .ReturnsAsync(account);
        _hasher.Setup(h => h.Verify("1234", "hash")).Returns(true);

        await CreateHandler().HandleAsync("1111222233334444", "1234", 1);

        account.FailedLoginAttempts.Should().Be(0);
    }

    [Fact]
    public async Task Login_Success_WritesTransaction()
    {
        var account = TestData.CreateAccount(id: 1);
        _accounts.Setup(r => r.GetByCardNumberAsync(It.IsAny<CardNumber>(), default))
                 .ReturnsAsync(account);
        _hasher.Setup(h => h.Verify("1234", "hash")).Returns(true);

        await CreateHandler().HandleAsync("1111222233334444", "1234", 1);

        _transactions.Verify(
            r => r.AddAsync(It.IsAny<Transaction>(), default),
            Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(default), Times.AtLeastOnce);
    }
}