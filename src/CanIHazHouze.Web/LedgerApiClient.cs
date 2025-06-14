namespace CanIHazHouze.Web;

public class LedgerApiClient(HttpClient httpClient)
{
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
            
            return null;
        }
        catch (HttpRequestException)
        {
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
}

public record AccountInfo(string Owner, decimal Balance, DateTimeOffset CreatedAt, DateTimeOffset LastUpdatedAt);
public record TransactionInfo(Guid Id, string Owner, decimal Amount, decimal BalanceAfter, string Description, DateTimeOffset CreatedAt);
public record BalanceUpdateRequest(decimal Amount, string Description);
