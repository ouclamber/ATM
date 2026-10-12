namespace Bankomat.Application.Abstractions;

public interface IBankConfig
{
    decimal MaxWithdrawalPerTransaction { get; }
    decimal MaxWithdrawalPerDay { get; }
    decimal MaxDepositPerTransaction { get; }
    decimal MaxTransferPerTransaction { get; }
    int DefaultAtmId { get; }
}