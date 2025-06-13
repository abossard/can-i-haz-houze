using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

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
    public Guid Id { get; set; }
    public string Owner { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty; // JSON serialized
    public string FileName { get; set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; set; }
}

// Database context
public class DocumentDbContext : DbContext
{
    public DocumentDbContext(DbContextOptions<DocumentDbContext> options) : base(options) { }
    
    public DbSet<DocumentEntity> Documents { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Owner).IsRequired().HasMaxLength(100);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Tags).HasMaxLength(2000);
            entity.HasIndex(e => e.Owner);
        });
    }
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

public class DocumentService : IDocumentService
{
    private readonly DocumentDbContext _context;
    private readonly DocumentStorageOptions _options;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(DocumentDbContext context, IOptions<DocumentStorageOptions> options, ILogger<DocumentService> logger)
    {
        _context = context;
        _options = options.Value;
        _logger = logger;
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
        
        // Save metadata to database
        var entity = new DocumentEntity
        {
            Id = id,
            Owner = owner,
            Tags = System.Text.Json.JsonSerializer.Serialize(tags),
            FileName = fileName,
            UploadedAt = DateTimeOffset.UtcNow
        };
        
        _context.Documents.Add(entity);
        await _context.SaveChangesAsync();
        
        _logger.LogInformation("Document {Id} uploaded for owner {Owner}", id, owner);
        
        return new DocumentMeta(id, owner, tags, fileName, entity.UploadedAt);
    }

    public async Task<IEnumerable<DocumentMeta>> GetDocumentsAsync(string owner)
    {
        var entities = await _context.Documents
            .Where(d => d.Owner == owner)
            .ToListAsync();
        
        return entities.Select(MapToDocumentMeta);
    }

    public async Task<DocumentMeta?> GetDocumentAsync(Guid id, string owner)
    {
        var entity = await _context.Documents
            .FirstOrDefaultAsync(d => d.Id == id && d.Owner == owner);
        
        return entity is null ? null : MapToDocumentMeta(entity);
    }

    public async Task<DocumentMeta?> UpdateDocumentTagsAsync(Guid id, string owner, List<string> tags)
    {
        var entity = await _context.Documents
            .FirstOrDefaultAsync(d => d.Id == id && d.Owner == owner);
        
        if (entity is null) return null;
        
        entity.Tags = System.Text.Json.JsonSerializer.Serialize(tags);
        await _context.SaveChangesAsync();
        
        _logger.LogInformation("Document {Id} tags updated for owner {Owner}", id, owner);
        
        return MapToDocumentMeta(entity);
    }

    public async Task<bool> DeleteDocumentAsync(Guid id, string owner)
    {
        var entity = await _context.Documents
            .FirstOrDefaultAsync(d => d.Id == id && d.Owner == owner);
        
        if (entity is null) return false;
        
        // Delete file from disk
        var path = Path.Combine(GetUserDir(owner), entity.FileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        
        // Delete from database
        _context.Documents.Remove(entity);
        await _context.SaveChangesAsync();
        
        _logger.LogInformation("Document {Id} deleted for owner {Owner}", id, owner);
        
        return true;
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
        var tags = string.IsNullOrEmpty(entity.Tags) 
            ? new List<string>() 
            : System.Text.Json.JsonSerializer.Deserialize<List<string>>(entity.Tags) ?? new List<string>();
        
        return new DocumentMeta(entity.Id, entity.Owner, tags, entity.FileName, entity.UploadedAt);
    }
}
