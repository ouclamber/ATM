using Bankomat.Application.Abstractions;
using Bankomat.Application.Accounts;
using Bankomat.Application.Operations;
using Bankomat.Application.Seed;
using Bankomat.Domain.Accounts;
using Bankomat.Domain.Atms;
using Bankomat.Domain.Transactions;
using Bankomat.Infrastructure.Configuration;
using Bankomat.Infrastructure.Persistence;
using Bankomat.Infrastructure.Persistence.Repositories;
using Bankomat.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Bankomat.Tests.Integration;

/// <summary>
/// Интеграционные тесты с настоящим PostgreSQL. Запускаются, только если задана переменная
/// BANKOMAT_TEST_CONNECTION (строка подключения к серверу; тест создаёт и удаляет отдельную БД).
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public const string EnvVar = "BANKOMAT_TEST_CONNECTION";

    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVar)))
            Skip = $"Не задана переменная {EnvVar} — интеграционные тесты с PostgreSQL пропущены";
    }
}

public sealed class PostgresFixture : IAsyncLifetime
{
    public ServiceProvider Services { get; private set; } = null!;
    public IConfiguration Configuration { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var baseCs = Environment.GetEnvironmentVariable(PostgresFactAttribute.EnvVar);
        if (string.IsNullOrWhiteSpace(baseCs))
            return; // тесты будут пропущены

        var dbName = $"atm_test_{Guid.NewGuid():N}";
        var cs = new Npgsql.NpgsqlConnectionStringBuilder(baseCs) { Database = dbName }.ConnectionString;

        Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ATM:DefaultATMId"] = "1",
            ["ATM:MaxWithdrawalPerTransaction"] = "20000",
            ["ATM:MaxWithdrawalPerDay"] = "50000",
            ["ATM:MaxDepositPerTransaction"] = "50000",
            ["ATM:MaxTransferPerTransaction"] = "100000",
            ["Seed:TestAccounts:0:CardNumber"] = "1111222233334444",
            ["Seed:TestAccounts:0:Pin"] = "4321",
            ["Seed:TestAccounts:0:Holder"] = "Иван",
            ["Seed:TestAccounts:0:InitialBalance"] = "50000",
            ["Seed:TestAccounts:1:CardNumber"] = "5555666677778888",
            ["Seed:TestAccounts:1:Pin"] = "2006",
            ["Seed:TestAccounts:1:Holder"] = "Мария",
            ["Seed:TestAccounts:1:InitialBalance"] = "75000",
        }).Build();

        var services = new ServiceCollection();
        services.AddSingleton(Configuration);
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<BankomatDbContext>(o => o.UseNpgsql(cs));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<IPinHasher, BCryptPinHasher>();
        services.AddSingleton<IBankConfig, BankConfig>();
        services.AddScoped<IBankAccountRepository, BankAccountRepository>();
        services.AddScoped<IAtmRepository, AtmRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<WithdrawHandler>();
        services.AddScoped<DepositHandler>();
        services.AddScoped<TransferHandler>();
        services.AddScoped<GetAccountSummaryHandler>();
        services.AddScoped<SeedHandler>();
        Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<BankomatDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<SeedHandler>().EnsureSeededAsync();
    }

    public async Task DisposeAsync()
    {
        if (Services is null) return;
        await using (var scope = Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<BankomatDbContext>().Database.EnsureDeletedAsync();
        await Services.DisposeAsync();
    }
}
