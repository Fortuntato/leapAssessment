namespace BankTransfer;

public sealed class InsufficientFundsException : InvalidOperationException
{
    public InsufficientFundsException(Guid accountId, decimal balance, decimal amount)
        : base($"Insufficient funds in account '{accountId}' for the transfer of {amount}. Available balance: {balance}.")
    {
        AccountId = accountId;
        Balance = balance;
        Amount = amount;
    }

    public Guid AccountId { get; }
    public decimal Balance { get; }
    public decimal Amount { get; }
}

public sealed class AccountNotFoundException : KeyNotFoundException
{
    public AccountNotFoundException(Guid accountId)
        : base($"Account '{accountId}' not found.")
    {
        AccountId = accountId;
    }

    public Guid AccountId { get; }
}
