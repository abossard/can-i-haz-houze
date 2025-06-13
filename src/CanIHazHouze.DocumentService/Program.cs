using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Configure document storage options
builder.Services.Configure<DocumentStorageOptions>(
    builder.Configuration.GetSection("DocumentStorage"));

// Add Entity Framework with SQLite
builder.Services.AddDbContext<DocumentDbContext>(options =>
{
    var storageOptions = builder.Configuration.GetSection("DocumentStorage").Get<DocumentStorageOptions>() 
                         ?? new DocumentStorageOptions();
    
    // Ensure the base directory exists
    Directory.CreateDirectory(storageOptions.BaseDirectory);
    
    var dbPath = Path.Combine(storageOptions.BaseDirectory, "documents.db");
    options.UseSqlite($"Data Source={dbPath}");
});

// Add document service
builder.Services.AddScoped<IDocumentService, DocumentServiceImpl>();

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
    context.Database.EnsureCreated();
}

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseStaticFiles(); // optional for serving files

// OpenAPI-tagged CRUD endpoints
app.MapPost("/documents", async (
    [FromForm, Required] string owner,
    IFormFile file,
    [FromForm] string? tags,
    IDocumentService documentService) =>
{
    try
    {
        var tagList = string.IsNullOrWhiteSpace(tags) 
            ? new List<string>() 
            : tags.Split(',').Select(t => t.Trim()).ToList();
        
        var documentMeta = await documentService.UploadDocumentAsync(owner, file, tagList);
        return Results.Created($"/documents/{documentMeta.Id}?owner={owner}", documentMeta);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ex.Message);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error uploading document for owner {Owner}", owner);
        return Results.Problem("An error occurred while uploading the document");
    }
})
.WithName("UploadDocument")
.Accepts<IFormFile>("multipart/form-data")
.Produces<DocumentMeta>(StatusCodes.Status201Created)
.Produces<string>(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status500InternalServerError)
.DisableAntiforgery();

app.MapGet("/documents", async ([Required] string owner, IDocumentService documentService) =>
{
    try
    {
        var documents = await documentService.GetDocumentsAsync(owner);
        return Results.Ok(documents);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error retrieving documents for owner {Owner}", owner);
        return Results.Problem("An error occurred while retrieving documents");
    }
})
.WithName("ListDocuments")
.Produces<IEnumerable<DocumentMeta>>()
.Produces(StatusCodes.Status500InternalServerError);

app.MapGet("/documents/{id}", async (Guid id, [Required] string owner, IDocumentService documentService) =>
{
    try
    {
        var document = await documentService.GetDocumentAsync(id, owner);
        return document is null ? Results.NotFound() : Results.Ok(document);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error retrieving document {Id} for owner {Owner}", id, owner);
        return Results.Problem("An error occurred while retrieving the document");
    }
})
.WithName("GetDocumentMeta")
.Produces<DocumentMeta>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status500InternalServerError);

app.MapPut("/documents/{id}", async ([FromRoute] Guid id, [Required] string owner, [FromBody] List<string> tags, IDocumentService documentService) =>
{
    try
    {
        var updated = await documentService.UpdateDocumentTagsAsync(id, owner, tags);
        return updated is null ? Results.NotFound() : Results.Ok(updated);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error updating document {Id} for owner {Owner}", id, owner);
        return Results.Problem("An error occurred while updating the document");
    }
})
.WithName("UpdateDocumentTags")
.Produces<DocumentMeta>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status500InternalServerError);

app.MapDelete("/documents/{id}", async (Guid id, [Required] string owner, IDocumentService documentService) =>
{
    try
    {
        var deleted = await documentService.DeleteDocumentAsync(id, owner);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error deleting document {Id} for owner {Owner}", id, owner);
        return Results.Problem("An error occurred while deleting the document");
    }
})
.WithName("DeleteDocument")
.Produces(StatusCodes.Status204NoContent)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status500InternalServerError);

app.MapDefaultEndpoints();

app.Run();

// Make Program class accessible for testing
public partial class Program { }

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

public class DocumentServiceImpl : IDocumentService
{
    private readonly DocumentDbContext _context;
    private readonly DocumentStorageOptions _options;
    private readonly ILogger<DocumentServiceImpl> _logger;

    public DocumentServiceImpl(DocumentDbContext context, IOptions<DocumentStorageOptions> options, ILogger<DocumentServiceImpl> logger)
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
