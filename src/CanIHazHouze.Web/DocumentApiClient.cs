namespace CanIHazHouze.Web;

public class DocumentApiClient(HttpClient httpClient)
{
    public async Task<DocumentMeta[]> GetDocumentsAsync(string owner, CancellationToken cancellationToken = default)
    {
        try
        {
            var documents = await httpClient.GetFromJsonAsync<DocumentMeta[]>($"/documents?owner={Uri.EscapeDataString(owner)}", cancellationToken);
            return documents ?? [];
        }
        catch (HttpRequestException)
        {
            // Return empty array if service is not available
            return [];
        }
    }

    public async Task<DocumentMeta?> GetDocumentAsync(Guid id, string owner, CancellationToken cancellationToken = default)
    {
        try
        {
            return await httpClient.GetFromJsonAsync<DocumentMeta>($"/documents/{id}?owner={Uri.EscapeDataString(owner)}", cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}

public record DocumentMeta(Guid Id, string Owner, List<string> Tags, string FileName, DateTimeOffset UploadedAt);
