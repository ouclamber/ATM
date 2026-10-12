using Bankomat.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Bankomat.Infrastructure.Configuration;

public class BankConfig : IBankConfig
{
    private readonly IConfiguration _cfg;
    public BankConfig(IConfiguration cfg) => _cfg = cfg;

    public decimal MaxWithdrawalPerTransaction => _cfg.GetValue<decimal>("ATM:MaxWithdrawalPerTransaction", 20000);
    public decimal MaxWithdrawalPerDay        => _cfg.GetValue<decimal>("ATM:MaxWithdrawalPerDay", 50000);
    public decimal MaxDepositPerTransaction   => _cfg.GetValue<decimal>("ATM:MaxDepositPerTransaction", 50000);
    public decimal MaxTransferPerTransaction  => _cfg.GetValue<decimal>("ATM:MaxTransferPerTransaction", 100000);
    public int DefaultAtmId                   => _cfg.GetValue<int>("ATM:DefaultATMId", 1);
}