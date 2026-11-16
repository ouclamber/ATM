using Bankomat.Application.Abstractions;
using Bankomat.Application.Operations;
using Bankomat.Domain.Accounts;
using Bankomat.Domain.Atms;
using Bankomat.Domain.Transactions;
using Bankomat.Tests.Application.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Bankomat.Tests.Application;

public class DepositHandlerTests
{
    private readonly Mock<IBankAccountRepository> _accounts = new();
    private readonly Mock<IAtmRepository> _atms = new();
    private readonly Mock<ITransactionRepository> _transactions = new();
    private readonly Mock<IBankConfig> _config = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private DepositHandler CreateHandler() =>
        new(
            _accounts.Object,
            _atms.Object,
            _transactions.Object,
            _config.Object,
            _uow.Object,
            NullLogger<DepositHandler>.Instance);

    private void SetupDefaultConfig()
    {
        _config.SetupGet(c => c.MaxDepositPerTransaction).Returns(50_000m);
    }

    [Fact]
    public async Task Deposit_ValidAmount_IncreasesBalance()
    {
        var account = TestData.CreateAccount(balance: 1000, id: 1);
        var atm = TestData.CreateAtm(id: 1);

        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(account);
        _atms.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(atm);
        SetupDefaultConfig();

        var banknotes = new Dictionary<int, int> { [5000] = 1 };
        var result = await CreateHandler().HandleAsync(1, 1, 5000, banknotes);

        result.IsSuccess.Should().BeTrue();
        account.Balance.Amount.Should().Be(6000);
    }

    [Fact]
    public async Task Deposit_AddsBanknotesToCassettes()
    {
        var account = TestData.CreateAccount(id: 1);
        var atm = TestData.CreateAtm(id: 1);
        var before5k = atm.Cassettes.First(c => c.Denomination == 5000).Count;

        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(account);
        _atms.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(atm);
        SetupDefaultConfig();

        var banknotes = new Dictionary<int, int> { [5000] = 5 };
        await CreateHandler().HandleAsync(1, 1, 25_000, banknotes);

        atm.Cassettes.First(c => c.Denomination == 5000).Count.Should().Be(before5k + 5);
    }

    [Fact]
    public async Task Deposit_BanknotesMismatchAmount_Fails()
    {
        var account = TestData.CreateAccount(id: 1);
        var atm = TestData.CreateAtm(id: 1);

        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(account);
        _atms.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(atm);
        SetupDefaultConfig();

        var banknotes = new Dictionary<int, int> { [5000] = 1 };
        var result = await CreateHandler().HandleAsync(1, 1, 3000, banknotes);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VALIDATION");
    }

    [Fact]
    public async Task Deposit_ExceedsLimit_Fails()
    {
        var account = TestData.CreateAccount(id: 1);
        var atm = TestData.CreateAtm(id: 1);

        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(account);
        _atms.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(atm);
        SetupDefaultConfig();

        var banknotes = new Dictionary<int, int> { [5000] = 20 };
        var result = await CreateHandler().HandleAsync(1, 1, 100_000, banknotes);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BUSINESS");
    }

    [Fact]
    public async Task Deposit_OnInactiveAtm_Fails()
    {
        var account = TestData.CreateAccount(id: 1);
        var atm = TestData.CreateAtm(id: 1, isActive: false);

        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(account);
        _atms.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(atm);
        SetupDefaultConfig();

        var banknotes = new Dictionary<int, int> { [5000] = 1 };
        var result = await CreateHandler().HandleAsync(1, 1, 5000, banknotes);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BUSINESS");
    }

    [Fact]
    public async Task Deposit_ExceedsCassetteCapacity_Fails()
    {
        var account = TestData.CreateAccount(id: 1);
        var atm = Atm.Create("A", "L").Value;
        atm.AddCassette(5000, 95, 100);
        typeof(Atm).GetProperty(nameof(Atm.Id))!.SetValue(atm, 1);

        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(account);
        _atms.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(atm);
        SetupDefaultConfig();

        var banknotes = new Dictionary<int, int> { [5000] = 10 };
        var result = await CreateHandler().HandleAsync(1, 1, 50_000, banknotes);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Deposit_NonExistentAccount_Fails()
    {
        _accounts.Setup(r => r.GetByIdAsync(999, default)).ReturnsAsync((BankAccount?)null);
        SetupDefaultConfig();

        var banknotes = new Dictionary<int, int> { [5000] = 1 };
        var result = await CreateHandler().HandleAsync(999, 1, 5000, banknotes);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Deposit_NonExistentAtm_Fails()
    {
        var account = TestData.CreateAccount(id: 1);
        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(account);
        _atms.Setup(r => r.GetByIdAsync(999, default)).ReturnsAsync((Atm?)null);
        SetupDefaultConfig();

        var banknotes = new Dictionary<int, int> { [5000] = 1 };
        var result = await CreateHandler().HandleAsync(1, 999, 5000, banknotes);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Deposit_Success_WritesTransaction()
    {
        var account = TestData.CreateAccount(id: 1);
        var atm = TestData.CreateAtm(id: 1);

        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(account);
        _atms.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(atm);
        SetupDefaultConfig();

        var banknotes = new Dictionary<int, int> { [5000] = 1 };
        await CreateHandler().HandleAsync(1, 1, 5000, banknotes);

        _transactions.Verify(
            r => r.AddAsync(It.IsAny<Transaction>(), default),
            Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Deposit_NegativeBanknoteCount_FailsAndChangesNothing()
    {
        // 5000×1 + 1000×(-1) = 4000 — раньше проходило проверку суммы
        var account = TestData.CreateAccount(balance: 1000, id: 1);
        var atm = TestData.CreateAtm(id: 1);
        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(account);
        _atms.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(atm);
        SetupDefaultConfig();

        var result = await CreateHandler().HandleAsync(
            1, 1, 4000, new Dictionary<int, int> { [5000] = 1, [1000] = -1 });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VALIDATION");
        account.Balance.Amount.Should().Be(1000);
        atm.Cassettes.First(c => c.Denomination == 5000).Count.Should().Be(100);
    }

    [Fact]
    public async Task Deposit_UnknownDenomination_Fails()
    {
        SetupDefaultConfig();

        var result = await CreateHandler().HandleAsync(
            1, 1, 300, new Dictionary<int, int> { [300] = 1 });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VALIDATION");
    }

    [Fact]
    public async Task Deposit_ZeroAmount_FailsWithoutDbAccess()
    {
        SetupDefaultConfig();

        var result = await CreateHandler().HandleAsync(1, 1, 0, new Dictionary<int, int>());

        result.IsFailure.Should().BeTrue();
        _accounts.VerifyNoOtherCalls();
        _atms.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Deposit_ConcurrencyConflict_ReturnsConflict()
    {
        var account = TestData.CreateAccount(balance: 1000, id: 1);
        var atm = TestData.CreateAtm(id: 1);
        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(account);
        _atms.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(atm);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException("conflict"));
        SetupDefaultConfig();

        var result = await CreateHandler().HandleAsync(1, 1, 1000, new Dictionary<int, int> { [1000] = 1 });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CONFLICT");
    }

    [Fact]
    public async Task Deposit_WritesCorrectBalanceBeforeAndAfter()
    {
        var account = TestData.CreateAccount(balance: 1000, id: 1);
        var atm = TestData.CreateAtm(id: 1);
        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(account);
        _atms.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(atm);
        SetupDefaultConfig();

        Transaction? captured = null;
        _transactions.Setup(r => r.AddAsync(It.IsAny<Transaction>(), default))
            .Callback<Transaction, CancellationToken>((t, _) => captured = t);

        await CreateHandler().HandleAsync(1, 1, 2500, new Dictionary<int, int> { [2000] = 1, [500] = 1 });

        captured!.BalanceBefore.Amount.Should().Be(1000);
        captured.BalanceAfter.Amount.Should().Be(3500);
    }
}
