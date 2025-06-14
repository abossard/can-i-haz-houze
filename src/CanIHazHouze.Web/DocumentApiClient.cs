using Microsoft.AspNetCore.Components.Forms;

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
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"DocumentAPI Error getting documents: {ex.Message}");
            return [];
        }
    }

    public async Task<DocumentMeta?> GetDocumentAsync(Guid id, string owner, CancellationToken cancellationToken = default)
    {
        try
        {
            return await httpClient.GetFromJsonAsync<DocumentMeta>($"/documents/{id}?owner={Uri.EscapeDataString(owner)}", cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"DocumentAPI Error getting document: {ex.Message}");
            return null;
        }
    }

    public async Task<DocumentMeta?> UploadDocumentAsync(string owner, IBrowserFile file, List<string> tags, CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            
            // Add owner
            content.Add(new StringContent(owner), "owner");
            
            // Add tags as comma-separated string
            if (tags.Any())
            {
                content.Add(new StringContent(string.Join(",", tags)), "tags");
            }
            
            // Add file
            var fileContent = new StreamContent(file.OpenReadStream(maxAllowedSize: 100 * 1024 * 1024)); // 100MB limit
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType ?? "application/octet-stream");
            content.Add(fileContent, "file", file.Name);

            var response = await httpClient.PostAsync("/documents", content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<DocumentMeta>(cancellationToken: cancellationToken);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"DocumentAPI Upload Error: {response.StatusCode} - {errorContent}");
                return null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DocumentAPI Upload Exception: {ex.Message}");
            return null;
        }
    }

    public async Task<DocumentMeta?> UpdateDocumentTagsAsync(Guid id, string owner, List<string> tags, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.PutAsJsonAsync($"/documents/{id}?owner={Uri.EscapeDataString(owner)}", tags, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<DocumentMeta>(cancellationToken: cancellationToken);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"DocumentAPI Update Error: {response.StatusCode} - {errorContent}");
                return null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DocumentAPI Update Exception: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> DeleteDocumentAsync(Guid id, string owner, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.DeleteAsync($"/documents/{id}?owner={Uri.EscapeDataString(owner)}", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DocumentAPI Delete Exception: {ex.Message}");
            return false;
        }
    }
}

public record DocumentMeta(Guid Id, string Owner, List<string> Tags, string FileName, DateTimeOffset UploadedAt);
