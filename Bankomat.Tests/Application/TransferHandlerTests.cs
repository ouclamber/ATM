using Bankomat.Application.Abstractions;
using Bankomat.Application.Operations;
using Bankomat.Domain.Accounts;
using Bankomat.Domain.Transactions;
using Bankomat.Domain.ValueObjects;
using Bankomat.Tests.Application.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Bankomat.Tests.Application;

public class TransferHandlerTests
{
    private readonly Mock<IBankAccountRepository> _accounts = new();
    private readonly Mock<ITransactionRepository> _transactions = new();
    private readonly Mock<IBankConfig> _config = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private TransferHandler CreateHandler() =>
        new(
            _accounts.Object,
            _transactions.Object,
            _config.Object,
            _uow.Object,
            NullLogger<TransferHandler>.Instance);

    private void SetupDefaultConfig(decimal maxTransfer = 100_000m)
    {
        _config.SetupGet(c => c.MaxTransferPerTransaction).Returns(maxTransfer);
    }

    [Fact]
    public async Task Transfer_ValidAccounts_MovesMoneyCorrectly()
    {
        var from = TestData.CreateAccount("1111222233334444", balance: 1000, id: 1);
        var to = TestData.CreateAccount("5555666677778888", balance: 500, id: 2);

        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(from);
        _accounts.Setup(r => r.GetByCardNumberAsync(It.IsAny<CardNumber>(), default)).ReturnsAsync(to);
        SetupDefaultConfig();

        var result = await CreateHandler().HandleAsync(1, 1, "5555666677778888", 300);

        result.IsSuccess.Should().BeTrue();
        from.Balance.Amount.Should().Be(700);
        to.Balance.Amount.Should().Be(800);
    }

    [Fact]
    public async Task Transfer_WritesTwoTransactions()
    {
        var from = TestData.CreateAccount("1111222233334444", balance: 1000, id: 1);
        var to = TestData.CreateAccount("5555666677778888", balance: 500, id: 2);

        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(from);
        _accounts.Setup(r => r.GetByCardNumberAsync(It.IsAny<CardNumber>(), default)).ReturnsAsync(to);
        SetupDefaultConfig();

        await CreateHandler().HandleAsync(1, 1, "5555666677778888", 300);

        _transactions.Verify(
            r => r.AddAsync(It.IsAny<Transaction>(), default),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Transfer_ToSelf_Fails()
    {
        var account = TestData.CreateAccount("1111222233334444", balance: 1000, id: 1);
        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(account);
        SetupDefaultConfig();

        var result = await CreateHandler().HandleAsync(1, 1, "1111222233334444", 300);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VALIDATION");
    }

    [Fact]
    public async Task Transfer_ExceedsLimit_Fails()
    {
        var account = TestData.CreateAccount(id: 1);
        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(account);
        SetupDefaultConfig(maxTransfer: 1000);

        var result = await CreateHandler().HandleAsync(1, 1, "5555666677778888", 5000);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BUSINESS");
    }

    [Fact]
    public async Task Transfer_InsufficientFunds_Fails()
    {
        var from = TestData.CreateAccount(balance: 100, id: 1);
        var to = TestData.CreateAccount("5555666677778888", balance: 500, id: 2);

        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(from);
        _accounts.Setup(r => r.GetByCardNumberAsync(It.IsAny<CardNumber>(), default)).ReturnsAsync(to);
        SetupDefaultConfig();

        var result = await CreateHandler().HandleAsync(1, 1, "5555666677778888", 300);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BUSINESS");
    }

    [Fact]
    public async Task Transfer_FromNonExistent_Fails()
    {
        _accounts.Setup(r => r.GetByIdAsync(999, default)).ReturnsAsync((BankAccount?)null);
        SetupDefaultConfig();

        var result = await CreateHandler().HandleAsync(999, 1, "5555666677778888", 100);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Transfer_ToNonExistent_Fails()
    {
        var from = TestData.CreateAccount(id: 1);
        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(from);
        _accounts.Setup(r => r.GetByCardNumberAsync(It.IsAny<CardNumber>(), default))
                 .ReturnsAsync((BankAccount?)null);
        SetupDefaultConfig();

        var result = await CreateHandler().HandleAsync(1, 1, "9999999999999999", 100);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Transfer_Success_CommitsTransaction()
    {
        var from = TestData.CreateAccount(balance: 1000, id: 1);
        var to = TestData.CreateAccount("5555666677778888", balance: 500, id: 2);

        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(from);
        _accounts.Setup(r => r.GetByCardNumberAsync(It.IsAny<CardNumber>(), default)).ReturnsAsync(to);
        SetupDefaultConfig();

        await CreateHandler().HandleAsync(1, 1, "5555666677778888", 300);

        _uow.Verify(u => u.BeginTransactionAsync(default), Times.Once);
        _uow.Verify(u => u.CommitAsync(default), Times.Once);
        _uow.Verify(u => u.RollbackAsync(default), Times.Never);
    }

    [Fact]
    public async Task Transfer_ExactBalance_Succeeds()
    {
        var from = TestData.CreateAccount(balance: 1000, id: 1);
        var to = TestData.CreateAccount("5555666677778888", balance: 0, id: 2);

        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(from);
        _accounts.Setup(r => r.GetByCardNumberAsync(It.IsAny<CardNumber>(), default)).ReturnsAsync(to);
        SetupDefaultConfig();

        var result = await CreateHandler().HandleAsync(1, 1, "5555666677778888", 1000);

        result.IsSuccess.Should().BeTrue();
        from.Balance.Amount.Should().Be(0);
        to.Balance.Amount.Should().Be(1000);
    }

    [Fact]
    public async Task Transfer_TotalMoneyIsConserved()
    {
        var from = TestData.CreateAccount(balance: 1000, id: 1);
        var to = TestData.CreateAccount("5555666677778888", balance: 500, id: 2);

        var totalBefore = from.Balance.Amount + to.Balance.Amount;

        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(from);
        _accounts.Setup(r => r.GetByCardNumberAsync(It.IsAny<CardNumber>(), default)).ReturnsAsync(to);
        SetupDefaultConfig();

        await CreateHandler().HandleAsync(1, 1, "5555666677778888", 300);

        var totalAfter = from.Balance.Amount + to.Balance.Amount;
        totalAfter.Should().Be(totalBefore, "перевод не должен создавать или уничтожать деньги");
    }
}