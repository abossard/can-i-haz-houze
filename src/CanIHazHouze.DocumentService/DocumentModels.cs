using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace CanIHazHouze.DocumentService;

// Configuration options
public class DocumentStorageOptions
{
    public string BaseDirectory { get; set; } = Path.Combine(Directory.GetCurrentDirectory(), "UserDocs");
}

// Data models
public record DocumentMeta(Guid Id, string Owner, List<string> Tags, string FileName, DateTimeOffset UploadedAt);

public class DocumentEntity
{
    public string id { get; set; } = string.Empty; // Cosmos DB id property
    public string owner { get; set; } = string.Empty; // Partition key (username)
    public Guid DocumentId { get; set; }
    public string Owner { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public string FileName { get; set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; set; }
    public string Type { get; set; } = "document"; // Document type discriminator
}

// Service interface and implementation
public interface IDocumentService
{
    Task<DocumentMeta> UploadDocumentAsync(string owner, IFormFile file, List<string> tags);
    Task<IEnumerable<DocumentMeta>> GetDocumentsAsync(string owner);
    Task<DocumentMeta?> GetDocumentAsync(Guid id, string owner);
    Task<DocumentMeta?> UpdateDocumentTagsAsync(Guid id, string owner, List<string> tags);
    Task<bool> DeleteDocumentAsync(Guid id, string owner);
}

public class DocumentServiceImpl : IDocumentService
{
    private readonly CosmosClient _cosmosClient;
    private readonly DocumentStorageOptions _options;
    private readonly ILogger<DocumentServiceImpl> _logger;
    private Container _container;

    public DocumentServiceImpl(CosmosClient cosmosClient, IOptions<DocumentStorageOptions> options, ILogger<DocumentServiceImpl> logger)
    {
        _cosmosClient = cosmosClient;
        _options = options.Value;
        _logger = logger;
        
        // Initialize container reference
        _container = _cosmosClient.GetContainer("houze", "documents");
    }

    public async Task<DocumentMeta> UploadDocumentAsync(string owner, IFormFile file, List<string> tags)
    {
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("Owner cannot be null or empty", nameof(owner));
        
        if (file is null || file.Length == 0)
            throw new ArgumentException("File cannot be null or empty", nameof(file));

        var userDir = GetUserDir(owner);
        var id = Guid.NewGuid();
        var fileName = $"{id}_{Path.GetFileName(file.FileName)}";
        var path = Path.Combine(userDir, fileName);
        
        // Save file to disk
        await using var fs = new FileStream(path, FileMode.Create);
        await file.CopyToAsync(fs);
        
        // Save metadata to Cosmos DB
        var entity = new DocumentEntity
        {
            id = id.ToString(),
            owner = owner, // Use username as partition key
            DocumentId = id,
            Owner = owner,
            Tags = tags,
            FileName = fileName,
            UploadedAt = DateTimeOffset.UtcNow
        };
        
        try
        {
            await _container.CreateItemAsync(entity, new PartitionKey(owner));
            
            _logger.LogInformation("Document {Id} uploaded for owner {Owner}", id, owner);
            
            return new DocumentMeta(id, owner, tags, fileName, entity.UploadedAt);
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to save document metadata for {Id}", id);
            
            // Clean up the file if database save fails
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            
            throw new InvalidOperationException("Failed to save document metadata", ex);
        }
    }

    public async Task<IEnumerable<DocumentMeta>> GetDocumentsAsync(string owner)
    {
        try
        {
            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.owner = @owner AND c.Type = @type ORDER BY c.UploadedAt DESC")
                .WithParameter("@owner", owner)
                .WithParameter("@type", "document");

            var iterator = _container.GetItemQueryIterator<DocumentEntity>(query, requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(owner)
            });

            var results = new List<DocumentEntity>();
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                results.AddRange(response);
            }

            return results.Select(MapToDocumentMeta);
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to retrieve documents for owner {Owner}", owner);
            return Enumerable.Empty<DocumentMeta>();
        }
    }

    public async Task<DocumentMeta?> GetDocumentAsync(Guid id, string owner)
    {
        try
        {
            var response = await _container.ReadItemAsync<DocumentEntity>(
                id.ToString(), 
                new PartitionKey(owner));
            
            return MapToDocumentMeta(response.Resource);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to retrieve document {Id} for owner {Owner}", id, owner);
            return null;
        }
    }

    public async Task<DocumentMeta?> UpdateDocumentTagsAsync(Guid id, string owner, List<string> tags)
    {
        try
        {
            var response = await _container.ReadItemAsync<DocumentEntity>(
                id.ToString(), 
                new PartitionKey(owner));
            
            var entity = response.Resource;
            entity.Tags = tags;
            
            await _container.ReplaceItemAsync(entity, entity.id, new PartitionKey(owner));
            
            _logger.LogInformation("Document {Id} tags updated for owner {Owner}", id, owner);
            
            return MapToDocumentMeta(entity);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to update document {Id} for owner {Owner}", id, owner);
            return null;
        }
    }

    public async Task<bool> DeleteDocumentAsync(Guid id, string owner)
    {
        try
        {
            // First get the document to get the filename
            var response = await _container.ReadItemAsync<DocumentEntity>(
                id.ToString(), 
                new PartitionKey(owner));
            
            var entity = response.Resource;
            
            // Delete from Cosmos DB
            await _container.DeleteItemAsync<DocumentEntity>(id.ToString(), new PartitionKey(owner));
            
            // Delete file from disk
            var path = Path.Combine(GetUserDir(owner), entity.FileName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            
            _logger.LogInformation("Document {Id} deleted for owner {Owner}", id, owner);
            
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to delete document {Id} for owner {Owner}", id, owner);
            return false;
        }
    }

    private string Sanitize(string username) =>
        Regex.Replace(username.Trim().ToLowerInvariant(), @"[^a-z0-9_\-]", "_");

    private string GetUserDir(string owner)
    {
        var dir = Path.Combine(_options.BaseDirectory, Sanitize(owner));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static DocumentMeta MapToDocumentMeta(DocumentEntity entity)
    {
        return new DocumentMeta(entity.DocumentId, entity.Owner, entity.Tags, entity.FileName, entity.UploadedAt);
    }
}
