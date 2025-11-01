namespace CanIHazHouze.Web;

public class LedgerApiClient(HttpClient httpClient)
{
    public string BaseUrl => httpClient.BaseAddress?.ToString().TrimEnd('/') ?? string.Empty;

    public async Task<AccountInfo?> GetAccountAsync(string owner, CancellationToken cancellationToken = default)
    {
        try
        {
            return await httpClient.GetFromJsonAsync<AccountInfo>($"/accounts/{Uri.EscapeDataString(owner)}", cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<AccountInfo?> UpdateBalanceAsync(string owner, decimal amount, string description, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new BalanceUpdateRequest(amount, description);
            var response = await httpClient.PostAsJsonAsync($"/accounts/{Uri.EscapeDataString(owner)}/balance", request, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<AccountInfo>(cancellationToken: cancellationToken);
            }
            else
            {
                // Log the error response for debugging
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"LedgerAPI Error: {response.StatusCode} - {errorContent}");
                return null;
            }
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"LedgerAPI HTTP Error: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"LedgerAPI Unexpected Error: {ex.Message}");
            return null;
        }
    }

    public async Task<TransactionInfo[]> GetTransactionsAsync(string owner, int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        try
        {
            var transactions = await httpClient.GetFromJsonAsync<TransactionInfo[]>(
                $"/accounts/{Uri.EscapeDataString(owner)}/transactions?skip={skip}&take={take}", 
                cancellationToken);
            return transactions ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    public async Task<AccountInfo?> ResetAccountAsync(string owner, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.PostAsync($"/accounts/{Uri.EscapeDataString(owner)}/reset", null, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<AccountInfo>(cancellationToken: cancellationToken);
            }
            
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<AccountInfo[]> GetRecentlyUpdatedAccountsAsync(int take = 10, CancellationToken cancellationToken = default)
    {
        try
        {
            var accounts = await httpClient.GetFromJsonAsync<AccountInfo[]>(
                $"/accounts/recent?take={take}", 
                cancellationToken);
            return accounts ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    public async Task<TransactionInfo[]> GetRecentTransactionsAsync(int take = 20, CancellationToken cancellationToken = default)
    {
        try
        {
            var transactions = await httpClient.GetFromJsonAsync<TransactionInfo[]>(
                $"/transactions/recent?take={take}", 
                cancellationToken);
            return transactions ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }
}

public record AccountInfo(string Owner, decimal Balance, DateTimeOffset CreatedAt, DateTimeOffset LastUpdatedAt);
public record TransactionInfo(Guid Id, string Owner, decimal Amount, decimal BalanceAfter, string Description, DateTimeOffset CreatedAt);
public record BalanceUpdateRequest(decimal Amount, string Description);
