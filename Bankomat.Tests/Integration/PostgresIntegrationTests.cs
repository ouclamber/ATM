using Bankomat.Application.Abstractions;
using Bankomat.Application.Accounts;
using Bankomat.Application.Operations;
using Bankomat.Application.Seed;
using Bankomat.Domain.Accounts;
using Bankomat.Domain.Atms;
using Bankomat.Domain.Transactions;
using Bankomat.Domain.ValueObjects;
using Bankomat.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bankomat.Tests.Integration;

public class PostgresIntegrationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fx;
    public PostgresIntegrationTests(PostgresFixture fx) => _fx = fx;

    private async Task<int> AccountIdAsync(string card)
    {
        await using var scope = _fx.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBankAccountRepository>();
        return (await repo.GetByCardNumberAsync(CardNumber.Create(card).Value))!.Id;
    }

    [PostgresFact]
    public async Task Seed_IsIdempotent_AndFindsExistingCardsInOneQuery()
    {
        await using (var scope = _fx.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<SeedHandler>().EnsureSeededAsync();

        await using var check = _fx.Services.CreateAsyncScope();
        var db = check.ServiceProvider.GetRequiredService<BankomatDbContext>();
        (await db.Atms.CountAsync()).Should().Be(1);
        (await db.Accounts.CountAsync(a => a.HolderName == "Иван")).Should().Be(1);

        var repo = check.ServiceProvider.GetRequiredService<IBankAccountRepository>();
        var existing = await repo.GetExistingCardNumbersAsync(new[]
        {
            CardNumber.Create("1111222233334444").Value,
            CardNumber.Create("0000000000000000").Value
        });
        existing.Should().BeEquivalentTo(new[] { "1111222233334444" });
    }

    [PostgresFact]
    public async Task Withdraw_PersistsBalanceCassettesAndJournal()
    {
        var accountId = await AccountIdAsync("5555666677778888");
        decimal before;
        decimal withdrawnBefore;
        await using (var scope = _fx.Services.CreateAsyncScope())
        {
            before = (await scope.ServiceProvider.GetRequiredService<GetAccountSummaryHandler>()
                .HandleAsync(accountId)).Value.Balance.Amount;
            withdrawnBefore = await scope.ServiceProvider.GetRequiredService<ITransactionRepository>()
                .GetWithdrawSumLast24hAsync(accountId);
        }

        await using (var scope = _fx.Services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<WithdrawHandler>()
                .HandleAsync(accountId, 1, 7000);
            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : "");
        }

        await using var check = _fx.Services.CreateAsyncScope();
        var account = await check.ServiceProvider.GetRequiredService<GetAccountSummaryHandler>()
            .HandleAsync(accountId);
        account.Value.Balance.Amount.Should().Be(before - 7000);

        var tx = check.ServiceProvider.GetRequiredService<ITransactionRepository>();
        (await tx.GetWithdrawSumLast24hAsync(accountId)).Should().Be(withdrawnBefore + 7000);
        var history = await tx.GetByAccountAsync(accountId, 30);
        history.Should().ContainSingle(t => t.Type == TransactionType.Withdraw && t.Amount.Amount == 7000);
    }

    [PostgresFact]
    public async Task ParallelUpdateOfSameAccount_IsDetectedByXmin()
    {
        var accountId = await AccountIdAsync("1111222233334444");

        // Два «банкомата» читают одну и ту же версию строки
        await using var s1 = _fx.Services.CreateAsyncScope();
        await using var s2 = _fx.Services.CreateAsyncScope();
        var a1 = await s1.ServiceProvider.GetRequiredService<IBankAccountRepository>().GetByIdAsync(accountId);
        var a2 = await s2.ServiceProvider.GetRequiredService<IBankAccountRepository>().GetByIdAsync(accountId);

        a1!.Debit(Money.Of(100)).IsSuccess.Should().BeTrue();
        a2!.Debit(Money.Of(100)).IsSuccess.Should().BeTrue();

        await s1.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        var act = () => s2.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        await act.Should().ThrowAsync<ConcurrencyConflictException>(
            "без токена конкурентности второе списание затёрло бы первое (lost update)");
    }

    [PostgresFact]
    public async Task ParallelWithdrawals_FromCassette_AreDetected()
    {
        await using var s1 = _fx.Services.CreateAsyncScope();
        await using var s2 = _fx.Services.CreateAsyncScope();
        var atm1 = await s1.ServiceProvider.GetRequiredService<IAtmRepository>().GetByIdAsync(1);
        var atm2 = await s2.ServiceProvider.GetRequiredService<IAtmRepository>().GetByIdAsync(1);

        var plan = new WithdrawalPlan(new Dictionary<int, int> { [100] = 1 });
        atm1!.ApplyWithdrawal(plan).IsSuccess.Should().BeTrue();
        atm2!.ApplyWithdrawal(plan).IsSuccess.Should().BeTrue();

        await s1.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        var act = () => s2.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [PostgresFact]
    public async Task Transfer_ConservesMoney()
    {
        var fromId = await AccountIdAsync("1111222233334444");
        var toId = await AccountIdAsync("5555666677778888");

        decimal Total(params AccountSummary[] a) => a.Sum(x => x.Balance.Amount);

        AccountSummary from0, to0;
        await using (var scope = _fx.Services.CreateAsyncScope())
        {
            var q = scope.ServiceProvider.GetRequiredService<GetAccountSummaryHandler>();
            from0 = (await q.HandleAsync(fromId)).Value;
            to0 = (await q.HandleAsync(toId)).Value;
        }

        await using (var scope = _fx.Services.CreateAsyncScope())
        {
            var r = await scope.ServiceProvider.GetRequiredService<TransferHandler>()
                .HandleAsync(fromId, 1, "5555-6666-7777-8888", 1234.56m);
            r.IsSuccess.Should().BeTrue(r.IsFailure ? r.Error.Message : "");
        }

        await using var check = _fx.Services.CreateAsyncScope();
        var q2 = check.ServiceProvider.GetRequiredService<GetAccountSummaryHandler>();
        var from1 = (await q2.HandleAsync(fromId)).Value;
        var to1 = (await q2.HandleAsync(toId)).Value;

        from1.Balance.Amount.Should().Be(from0.Balance.Amount - 1234.56m);
        Total(from1, to1).Should().Be(Total(from0, to0));
    }
}
