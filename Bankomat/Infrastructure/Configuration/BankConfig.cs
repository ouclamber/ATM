using Bankomat.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Bankomat.Infrastructure.Configuration;

/// <summary>
/// Лимиты банкомата. Читаются и проверяются один раз при старте:
/// ошибка в appsettings.json всплывает сразу, а не посреди операции клиента.
/// </summary>
public class BankConfig : IBankConfig
{
    public decimal MaxWithdrawalPerTransaction { get; }
    public decimal MaxWithdrawalPerDay { get; }
    public decimal MaxDepositPerTransaction { get; }
    public decimal MaxTransferPerTransaction { get; }
    public int DefaultAtmId { get; }

    public BankConfig(IConfiguration cfg)
    {
        MaxWithdrawalPerTransaction = Positive(cfg, "ATM:MaxWithdrawalPerTransaction", 20000);
        MaxWithdrawalPerDay         = Positive(cfg, "ATM:MaxWithdrawalPerDay", 50000);
        MaxDepositPerTransaction    = Positive(cfg, "ATM:MaxDepositPerTransaction", 50000);
        MaxTransferPerTransaction   = Positive(cfg, "ATM:MaxTransferPerTransaction", 100000);
        DefaultAtmId                = (int)Positive(cfg, "ATM:DefaultATMId", 1);

        if (MaxWithdrawalPerTransaction > MaxWithdrawalPerDay)
            throw new InvalidOperationException(
                "ATM:MaxWithdrawalPerTransaction не может быть больше ATM:MaxWithdrawalPerDay");
    }

    private static decimal Positive(IConfiguration cfg, string key, decimal fallback)
    {
        var value = cfg.GetValue(key, fallback);
        if (value <= 0)
            throw new InvalidOperationException($"Параметр {key} должен быть больше 0 (сейчас {value})");
        return value;
    }
}
