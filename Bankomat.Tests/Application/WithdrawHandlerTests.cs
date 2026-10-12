using Bankomat.Application.Abstractions;
using Bankomat.Application.Operations;
using Bankomat.Domain.Accounts;
using Bankomat.Domain.Atms;
using Bankomat.Domain.Transactions;
using Bankomat.Domain.ValueObjects;
using Bankomat.Tests.Application.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Bankomat.Tests.Application;

public class WithdrawHandlerTests
{
    private readonly Mock<IBankAccountRepository> _accounts = new();
    private readonly Mock<IAtmRepository> _atms = new();
    private readonly Mock<ITransactionRepository> _transactions = new();
    private readonly Mock<IBankConfig> _config = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private WithdrawHandler CreateHandler() =>
        new(
            _accounts.Object,
            _atms.Object,
            _transactions.Object,
            _config.Object,
            _uow.Object,
            NullLogger<WithdrawHandler>.Instance);

    private void SetupDefaultConfig()
    {
        _config.SetupGet(c => c.MaxWithdrawalPerTransaction).Returns(20_000m);
        _config.SetupGet(c => c.MaxWithdrawalPerDay).Returns(50_000m);
    }

    [Fact]
    public async Task Withdraw_ValidRequest_DecreasesBalanceAndCassettes()
    {
        var account = TestData.CreateAccount(balance: 1000, id: 1);
        var atm = TestData.CreateAtm(id: 1);

        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(account);
        _atms.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(atm);
        _transactions.Setup(r => r.GetWithdrawSumLast24hAsync(1, default)).ReturnsAsync(0m);
        SetupDefaultConfig();

        var result = await CreateHandler().HandleAsync(1, 1, 1000);

        result.IsSuccess.Should().BeTrue();
        result.Value.NewBalance.Amount.Should().Be(0);
        atm.Cassettes.First(c => c.Denomination == 1000).Count.Should().Be(99);
    }

    [Fact]
    public async Task Withdraw_ReturnsBanknotes()
    {
        var account = TestData.CreateAccount(balance: 5000, id: 1);
        var atm = TestData.CreateAtm(id: 1);

        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(account);
        _atms.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(atm);
        _transactions.Setup(r => r.GetWithdrawSumLast24hAsync(1, default)).ReturnsAsync(0m);
        SetupDefaultConfig();

        var result = await CreateHandler().HandleAsync(1, 1, 5000);

        result.IsSuccess.Should().BeTrue();
        result.Value.Banknotes.Should().ContainKey(5000);
        result.Value.Banknotes[5000].Should().Be(1);
    }

    [Fact]
    public async Task Withdraw_ExceedsPerTransactionLimit_Fails()
    {
        var account = TestData.CreateAccount(balance: 100_000, id: 1);
        var atm = TestData.CreateAtm(id: 1);

        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(account);
        _atms.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(atm);
        SetupDefaultConfig();

        var result = await CreateHandler().HandleAsync(1, 1, 25_000);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BUSINESS");
    }

    [Fact]
    public async Task Withdraw_ExceedsDailyLimit_Fails()
    {
        var account = TestData.CreateAccount(balance: 100_000, id: 1);
        var atm = TestData.CreateAtm(id: 1);

        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(account);
        _atms.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(atm);
        _transactions.Setup(r => r.GetWithdrawSumLast24hAsync(1, default)).ReturnsAsync(45_000m);
        SetupDefaultConfig();

        var result = await CreateHandler().HandleAsync(1, 1, 10_000);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BUSINESS");
    }

    [Fact]
    public async Task Withdraw_NotMultipleOf100_Fails()
    {
        var account = TestData.CreateAccount(balance: 1000, id: 1);
        var atm = TestData.CreateAtm(id: 1);

        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(account);
        _atms.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(atm);
        SetupDefaultConfig();

        var result = await CreateHandler().HandleAsync(1, 1, 150);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VALIDATION");
    }

    [Fact]
    public async Task Withdraw_InsufficientBalance_Fails()
    {
        var account = TestData.CreateAccount(balance: 100, id: 1);
        var atm = TestData.CreateAtm(id: 1);

        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(account);
        _atms.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(atm);
        SetupDefaultConfig();

        var result = await CreateHandler().HandleAsync(1, 1, 500);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BUSINESS");
    }

    [Fact]
    public async Task Withdraw_OnInactiveAtm_Fails()
    {
        var account = TestData.CreateAccount(balance: 1000, id: 1);
        var atm = TestData.CreateAtm(id: 1, isActive: false);

        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(account);
        _atms.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(atm);
        SetupDefaultConfig();

        var result = await CreateHandler().HandleAsync(1, 1, 500);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BUSINESS");
    }

    [Fact]
    public async Task Withdraw_InsufficientCassettes_Fails()
    {
        var account = TestData.CreateAccount(balance: 100_000, id: 1);
        var atm = Atm.Create("A", "L").Value;
        atm.AddCassette(5000, 1, 100);
        typeof(Atm).GetProperty(nameof(Atm.Id))!.SetValue(atm, 1);

        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(account);
        _atms.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(atm);
        _transactions.Setup(r => r.GetWithdrawSumLast24hAsync(1, default)).ReturnsAsync(0m);
        SetupDefaultConfig();

        var result = await CreateHandler().HandleAsync(1, 1, 15_000);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Withdraw_AccountNotFound_Fails()
    {
        _accounts.Setup(r => r.GetByIdAsync(999, default)).ReturnsAsync((BankAccount?)null);
        SetupDefaultConfig();

        var result = await CreateHandler().HandleAsync(999, 1, 500);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Withdraw_Success_SavesChanges()
    {
        var account = TestData.CreateAccount(balance: 1000, id: 1);
        var atm = TestData.CreateAtm(id: 1);

        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(account);
        _atms.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(atm);
        _transactions.Setup(r => r.GetWithdrawSumLast24hAsync(1, default)).ReturnsAsync(0m);
        SetupDefaultConfig();

        await CreateHandler().HandleAsync(1, 1, 500);

        _uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Withdraw_WritesTransactionWithCorrectBalance()
    {
        var account = TestData.CreateAccount(balance: 1000, id: 1);
        var atm = TestData.CreateAtm(id: 1);

        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(account);
        _atms.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(atm);
        _transactions.Setup(r => r.GetWithdrawSumLast24hAsync(1, default)).ReturnsAsync(0m);
        SetupDefaultConfig();

        Transaction? captured = null;
        _transactions
            .Setup(r => r.AddAsync(It.IsAny<Transaction>(), default))
            .Callback<Transaction, CancellationToken>((t, _) => captured = t);

        await CreateHandler().HandleAsync(1, 1, 300);

        captured.Should().NotBeNull();
        captured!.BalanceBefore.Amount.Should().Be(1000);
        captured.BalanceAfter.Amount.Should().Be(700);
    }
}