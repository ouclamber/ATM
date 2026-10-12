namespace Bankomat.Domain.Transactions;

public enum TransactionType
{
    Login = 1,
    Withdraw = 2,
    Deposit = 3,
    TransferOut = 4,
    TransferIn = 5,
    BalanceCheck = 6
}