using Bankomat.Application.Abstractions;
using Bankomat.Domain.Accounts;
using Bankomat.Domain.Atms;
using Bankomat.Domain.Common;
using Bankomat.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Bankomat.Application.Seed;

public class SeedHandler
{
    private readonly IBankAccountRepository _accounts;
    private readonly IAtmRepository _atms;
    private readonly IPinHasher _hasher;
    private readonly IBankConfig _config;
    private readonly IConfiguration _configuration;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<SeedHandler> _logger;

    public SeedHandler(
        IBankAccountRepository accounts,
        IAtmRepository atms,
        IPinHasher hasher,
        IBankConfig config,
        IConfiguration configuration,
        IUnitOfWork uow,
        ILogger<SeedHandler> logger)
    {
        _accounts = accounts;
        _atms = atms;
        _hasher = hasher;
        _config = config;
        _configuration = configuration;
        _uow = uow;
        _logger = logger;
    }

    public async Task EnsureSeededAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Seed: проверка исходных данных");
        await SeedAtmIfNeededAsync(ct);
        await SeedAccountsIfNeededAsync(ct);
    }

    private async Task SeedAtmIfNeededAsync(CancellationToken ct)
    {
        var atmId = _config.DefaultAtmId;
        var atm = await _atms.GetByIdAsync(atmId, ct);
        if (atm is not null)
        {
            _logger.LogDebug("Seed: банкомат {AtmId} уже существует, пропуск", atmId);
            return;
        }

        _logger.LogInformation("Seed: создание банкомата {AtmId}", atmId);

        var atmResult = Atm.Create("Главный банкомат", "Москва, ул. Примерная, д. 1");
        if (atmResult.IsFailure)
            throw new DomainException(atmResult.Error.Message);

        var createdAtm = atmResult.Value;

        var cassettes = new (int denom, int count, int max)[]
        {
            (5000, 100, 200),
            (2000, 150, 250),
            (1000, 200, 300),
            (500,  300, 400),
            (100,  500, 1000)
        };

        foreach (var (denom, count, max) in cassettes)
        {
            var addResult = createdAtm.AddCassette(denom, count, max);
            if (addResult.IsFailure)
                throw new DomainException(addResult.Error.Message);
        }

        await _atms.AddAsync(createdAtm, ct);
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Seed: банкомат создан с {CassetteCount} кассетами, всего {TotalCash}",
            cassettes.Length, createdAtm.TotalCash());
    }

    private async Task SeedAccountsIfNeededAsync(CancellationToken ct)
    {
        var accounts = _configuration
            .GetSection("Seed:TestAccounts")
            .Get<List<SeedAccount>>() ?? new List<SeedAccount>();

        if (accounts.Count == 0)
        {
            _logger.LogWarning("Seed: секция Seed:TestAccounts пуста, клиенты не создаются");
            return;
        }

        _logger.LogInformation(
            "Seed: проверка {Count} тестовых клиентов", accounts.Count);

        var created = 0;

        foreach (var seed in accounts)
        {
            var cardResult = CardNumber.Create(seed.CardNumber);
            if (cardResult.IsFailure)
                throw new DomainException($"Неверный номер карты в seed: {seed.CardNumber}");

            var existing = await _accounts.GetByCardNumberAsync(cardResult.Value, ct);
            if (existing is not null)
            {
                _logger.LogDebug(
                    "Seed: карта {Card} уже существует, пропуск",
                    MaskCard(seed.CardNumber));
                continue;
            }

            var moneyResult = Money.Create(seed.InitialBalance);
            if (moneyResult.IsFailure)
                throw new DomainException($"Неверный баланс для {seed.CardNumber}");

            var hash = _hasher.Hash(seed.Pin);

            var accountResult = BankAccount.Create(
                cardResult.Value, hash, seed.Holder, moneyResult.Value);
            if (accountResult.IsFailure)
                throw new DomainException(accountResult.Error.Message);

            await _accounts.AddAsync(accountResult.Value, ct);
            created++;

            _logger.LogInformation(
                "Seed: создан клиент {Holder}, карта {Card}, баланс {Balance}",
                seed.Holder, MaskCard(seed.CardNumber), moneyResult.Value);
        }

        if (created > 0)
        {
            await _uow.SaveChangesAsync(ct);
            _logger.LogInformation("Seed: создано {Count} новых клиентов", created);
        }
        else
        {
            _logger.LogInformation("Seed: все клиенты уже существуют");
        }
    }

    private static string MaskCard(string card) =>
        card.Length >= 4 ? $"**** **** **** {card[^4..]}" : "****";

    private sealed class SeedAccount
    {
        public string CardNumber { get; set; } = string.Empty;
        public string Pin { get; set; } = string.Empty;
        public string Holder { get; set; } = string.Empty;
        public decimal InitialBalance { get; set; }
    }
}