using System.ComponentModel;
using ModelContextProtocol.Server;
using CanIHazHouze.LedgerService;
using Microsoft.Extensions.Logging;

namespace CanIHazHouze.LedgerService.McpTools;

[McpServerToolType]
public class LedgerTools
{
    private readonly ILedgerService _ledgerService;

    public LedgerTools(ILedgerService ledgerService)
    {
        _ledgerService = ledgerService;
    }

    [McpServerTool]
    [Description("Retrieve account information including balance and timestamps")]
    public async Task<AccountInfo?> GetAccountInfo(
        [Description("The account owner's username")] string owner)
    {
        return await _ledgerService.GetAccountAsync(owner);
    }

    [McpServerTool]
    [Description("Update account balance by adding or subtracting the specified amount")]
    public async Task<AccountInfo?> UpdateAccountBalance(
        [Description("The account owner's username")] string owner,
        [Description("The amount to add (positive) or subtract (negative)")] decimal amount,
        [Description("Description of the transaction")] string description)
    {
        return await _ledgerService.UpdateBalanceAsync(owner, amount, description);
    }

    [McpServerTool]
    [Description("Retrieve transaction history for a user account with pagination support")]
    public async Task<IEnumerable<TransactionInfo>> GetTransactionHistory(
        [Description("The account owner's username")] string owner,
        [Description("Number of transactions to skip")] int skip = 0,
        [Description("Number of transactions to take")] int take = 10)
    {
        return await _ledgerService.GetTransactionsAsync(owner, skip, take);
    }

    [McpServerTool]
    [Description("Reset account to initial state with new random balance")]
    public async Task<AccountInfo?> ResetAccount(
        [Description("The account owner's username")] string owner)
    {
        return await _ledgerService.ResetAccountAsync(owner);
    }

    [McpServerTool]
    [Description("Get recently updated user accounts ordered by last update time (system-wide view)")]
    public async Task<IEnumerable<AccountInfo>> GetRecentlyUpdatedAccounts(
        [Description("Maximum number of accounts to return (default: 10, max: 100)")] int take = 10)
    {
        return await _ledgerService.GetRecentlyUpdatedAccountsAsync(take);
    }

    [McpServerTool]
    [Description("Get recent transactions across all users ordered by timestamp (system-wide view)")]
    public async Task<IEnumerable<TransactionInfo>> GetRecentTransactions(
        [Description("Maximum number of transactions to return (default: 20, max: 100)")] int take = 20)
    {
        return await _ledgerService.GetRecentTransactionsAsync(take);
    }
}
