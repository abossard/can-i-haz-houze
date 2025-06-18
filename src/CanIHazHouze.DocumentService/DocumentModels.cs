using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace CanIHazHouze.DocumentService;

// Configuration options - now simplified since we use Azure Blob Storage
public class DocumentStorageOptions
{
    public string ContainerName { get; set; } = "documents";
    public long MaxFileSizeBytes { get; set; } = 100 * 1024 * 1024; // 100MB default
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
    Task<Stream?> GetDocumentContentAsync(Guid id, string owner);
    string GetBlobName(Guid id, string fileName);
}

public class DocumentServiceImpl : IDocumentService
{
    private readonly CosmosClient _cosmosClient;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly DocumentStorageOptions _options;
    private readonly ILogger<DocumentServiceImpl> _logger;
    private Container _container;
    private BlobContainerClient _blobContainer;

    public DocumentServiceImpl(
        CosmosClient cosmosClient, 
        BlobServiceClient blobServiceClient,
        IOptions<DocumentStorageOptions> options, 
        ILogger<DocumentServiceImpl> logger)
    {
        _cosmosClient = cosmosClient;
        _blobServiceClient = blobServiceClient;
        _options = options.Value;
        _logger = logger;
        
        // Initialize container reference
        _container = _cosmosClient.GetContainer("houze", "documents");
        
        // Initialize blob container reference
        _blobContainer = _blobServiceClient.GetBlobContainerClient(_options.ContainerName);
    }

    public async Task<DocumentMeta> UploadDocumentAsync(string owner, IFormFile file, List<string> tags)
    {
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("Owner cannot be null or empty", nameof(owner));
        
        if (file is null || file.Length == 0)
            throw new ArgumentException("File cannot be null or empty", nameof(file));

        if (file.Length > _options.MaxFileSizeBytes)
            throw new ArgumentException($"File size exceeds maximum allowed size of {_options.MaxFileSizeBytes} bytes", nameof(file));

        var id = Guid.NewGuid();
        var fileName = $"{id}_{Path.GetFileName(file.FileName)}";
        var blobName = GetBlobName(id, fileName);
        var uploadedAt = DateTimeOffset.UtcNow;
        
        try
        {
            // Ensure container exists
            await _blobContainer.CreateIfNotExistsAsync(PublicAccessType.None);
            
            // Upload file to blob storage
            var blobClient = _blobContainer.GetBlobClient(blobName);
            
            // Set blob metadata
            var metadata = new Dictionary<string, string>
            {
                ["originalFileName"] = Path.GetFileName(file.FileName) ?? "",
                ["uploadedAt"] = uploadedAt.ToString("O"),
                ["owner"] = owner,
                ["documentId"] = id.ToString()
            };
            
            // Set blob index tags for efficient querying
            var blobTags = new Dictionary<string, string>
            {
                ["owner"] = SanitizeTagValue(owner),
                ["documentId"] = id.ToString(),
                ["uploadYear"] = uploadedAt.Year.ToString(),
                ["fileType"] = Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant(),
                ["contentType"] = file.ContentType ?? "application/octet-stream"
            };
            
            // Upload with metadata and tags
            var blobUploadOptions = new BlobUploadOptions
            {
                Metadata = metadata,
                Tags = blobTags,
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = file.ContentType ?? "application/octet-stream"
                }
            };
            
            await using var fileStream = file.OpenReadStream();
            await blobClient.UploadAsync(fileStream, blobUploadOptions);
            
            // Save metadata to Cosmos DB
            var entity = new DocumentEntity
            {
                id = id.ToString(),
                owner = owner, // Use username as partition key
                DocumentId = id,
                Owner = owner,
                Tags = tags,
                FileName = fileName,
                UploadedAt = uploadedAt
            };
            
            await _container.CreateItemAsync(entity, new PartitionKey(owner));
            
            _logger.LogInformation("Document {Id} uploaded to blob storage and metadata saved for owner {Owner}", id, owner);
            
            return new DocumentMeta(id, owner, tags, fileName, uploadedAt);
        }
        catch (Exception ex) when (!(ex is CosmosException))
        {
            _logger.LogError(ex, "Failed to upload document {Id} to blob storage for owner {Owner}", id, owner);
            
            // Clean up blob if it was uploaded but Cosmos DB save failed
            try
            {
                var blobClient = _blobContainer.GetBlobClient(blobName);
                await blobClient.DeleteIfExistsAsync();
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Failed to clean up blob {BlobName} after upload failure", blobName);
            }
            
            throw new InvalidOperationException("Failed to upload document to blob storage", ex);
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to save document metadata for {Id}", id);
            
            // Clean up blob if database save fails
            try
            {
                var blobClient = _blobContainer.GetBlobClient(blobName);
                await blobClient.DeleteIfExistsAsync();
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Failed to clean up blob {BlobName} after metadata save failure", blobName);
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
            var blobName = GetBlobName(id, entity.FileName);
            
            // Delete from Cosmos DB first
            await _container.DeleteItemAsync<DocumentEntity>(id.ToString(), new PartitionKey(owner));
            
            // Delete blob from storage
            var blobClient = _blobContainer.GetBlobClient(blobName);
            await blobClient.DeleteIfExistsAsync();
            
            _logger.LogInformation("Document {Id} deleted from both blob storage and database for owner {Owner}", id, owner);
            
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete document blob {Id} for owner {Owner}", id, owner);
            return false;
        }
    }

    public async Task<Stream?> GetDocumentContentAsync(Guid id, string owner)
    {
        try
        {
            // First verify the document exists and belongs to the owner
            var response = await _container.ReadItemAsync<DocumentEntity>(
                id.ToString(), 
                new PartitionKey(owner));
            
            var entity = response.Resource;
            var blobName = GetBlobName(id, entity.FileName);
            
            // Get blob content
            var blobClient = _blobContainer.GetBlobClient(blobName);
            var blobResponse = await blobClient.DownloadStreamingAsync();
            
            return blobResponse.Value.Content;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve document content {Id} for owner {Owner}", id, owner);
            return null;
        }
    }

    public string GetBlobName(Guid id, string fileName)
    {
        // Use virtual directory structure: owner/documentId_fileName
        // Note: owner will be added by the calling method when needed
        return $"{id}_{Path.GetFileName(fileName)}";
    }

    private static string SanitizeTagValue(string value)
    {
        // Azure blob tags have restrictions: alphanumeric, spaces, and some special chars
        // Must be 1-256 characters, no leading/trailing spaces
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";
            
        var sanitized = Regex.Replace(value.Trim(), @"[^a-zA-Z0-9\s\-_.]", "_");
        return sanitized.Length > 256 ? sanitized[..256] : sanitized;
    }

    public string GetDocumentPath(Guid id, string owner, string fileName)
    {
        // Legacy method - now returns blob name for compatibility
        return GetBlobName(id, fileName);
    }

    private string Sanitize(string username) =>
        Regex.Replace(username.Trim().ToLowerInvariant(), @"[^a-z0-9_\-]", "_");

    private string GetUserDir(string owner)
    {
        // Legacy method - kept for compatibility but no longer used
        return Sanitize(owner);
    }

    private static DocumentMeta MapToDocumentMeta(DocumentEntity entity)
    {
        return new DocumentMeta(entity.DocumentId, entity.Owner, entity.Tags, entity.FileName, entity.UploadedAt);
    }
}
