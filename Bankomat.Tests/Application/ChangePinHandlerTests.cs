using Bankomat.Application.Abstractions;
using Bankomat.Application.Authentication;
using Bankomat.Domain.Accounts;
using Bankomat.Tests.Application.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Bankomat.Tests.Application;

public class ChangePinHandlerTests
{
    private readonly Mock<IBankAccountRepository> _accounts = new();
    private readonly Mock<IPinHasher> _hasher = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private ChangePinHandler CreateHandler() =>
        new(
            _accounts.Object,
            _hasher.Object,
            _uow.Object,
            NullLogger<ChangePinHandler>.Instance);

    [Fact]
    public async Task ChangePin_ValidChange_Succeeds()
    {
        var account = TestData.CreateAccount(pinHash: "oldHash", id: 1);
        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(account);
        _hasher.Setup(h => h.Verify("1234", "oldHash")).Returns(true);
        _hasher.Setup(h => h.Hash("5678")).Returns("newHash");

        var result = await CreateHandler().HandleAsync(1, "1234", "5678");

        result.IsSuccess.Should().BeTrue();
        account.PinHash.Should().Be("newHash");
    }

    [Fact]
    public async Task ChangePin_WrongOldPin_Fails()
    {
        var account = TestData.CreateAccount(pinHash: "oldHash", id: 1);
        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(account);
        _hasher.Setup(h => h.Verify("0000", "oldHash")).Returns(false);

        var result = await CreateHandler().HandleAsync(1, "0000", "5678");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task ChangePin_SamePin_Fails()
    {
        var account = TestData.CreateAccount(pinHash: "oldHash", id: 1);
        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(account);
        _hasher.Setup(h => h.Verify("1234", "oldHash")).Returns(true);

        var result = await CreateHandler().HandleAsync(1, "1234", "1234");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VALIDATION");
    }

    [Fact]
    public async Task ChangePin_InvalidNewPinFormat_Fails()
    {
        var account = TestData.CreateAccount(id: 1);
        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(account);

        var result = await CreateHandler().HandleAsync(1, "1234", "12");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VALIDATION");
    }

    [Fact]
    public async Task ChangePin_NonExistentAccount_Fails()
    {
        _accounts.Setup(r => r.GetByIdAsync(999, default)).ReturnsAsync((BankAccount?)null);

        var result = await CreateHandler().HandleAsync(999, "1234", "5678");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task ChangePin_Success_SavesChanges()
    {
        var account = TestData.CreateAccount(pinHash: "oldHash", id: 1);
        _accounts.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(account);
        _hasher.Setup(h => h.Verify("1234", "oldHash")).Returns(true);
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("newHash");

        await CreateHandler().HandleAsync(1, "1234", "5678");

        _uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }
}