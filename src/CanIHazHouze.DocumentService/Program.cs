using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using System.Text.RegularExpressions;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel;
using CanIHazHouze.DocumentService;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Configure document storage options
builder.Services.Configure<DocumentStorageOptions>(
    builder.Configuration.GetSection("DocumentStorage"));

// Add Azure Cosmos DB using Aspire
builder.AddAzureCosmosClient("cosmos");

// Add document service
builder.Services.AddScoped<IDocumentService, DocumentServiceImpl>();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

// Use CORS
app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
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
.WithSummary("Upload a new document")
.WithDescription("""
    Uploads a new document to the storage system with optional metadata tags.
    
    **Key Features:**
    - Supports all file formats (images, PDFs, documents, etc.)
    - Configurable file size limits (default: 100MB)
    - Automatic GUID-based unique filename generation
    - Tag-based organization system
    - Per-user document isolation
    
    **Form Data Parameters:**
    - `owner` (required): Username/identifier for document ownership
    - `file` (required): The file to upload (multipart/form-data)
    - `tags` (optional): Comma-separated list of tags for organization
    
    **Examples:**
    - Basic upload: owner="john_doe", file=report.pdf
    - With tags: owner="john_doe", file=receipt.jpg, tags="expense, 2024, restaurant"
    
    **Response:**
    Returns the complete document metadata including generated ID, upload timestamp, and file information.
    """)
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Document Management" }];
    operation.RequestBody.Description = "Document upload form with file and metadata";
    
    // Add parameter descriptions
    var ownerParam = operation.Parameters.FirstOrDefault(p => p.Name == "owner");
    if (ownerParam != null)
    {
        ownerParam.Description = "Username or identifier of the document owner. Used for access control and organization.";
        ownerParam.Required = true;
        ownerParam.Example = new Microsoft.OpenApi.Any.OpenApiString("john_doe");
    }
    
    return operation;
})
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
.WithSummary("List all documents for a user")
.WithDescription("""
    Retrieves all documents owned by the specified user, including their metadata.
    
    **Key Features:**
    - Returns complete document metadata for all user's documents
    - Includes tags, upload timestamps, and file information
    - Results are ordered by upload date (most recent first)
    - No pagination - returns all documents for the user
    
    **Parameters:**
    - `owner` (required): Username/identifier to list documents for
    
    **Response:**
    Returns an array of document metadata objects. Empty array if no documents found.
    
    **Example Response:**
    ```json
    [
        {
            "id": "123e4567-e89b-12d3-a456-426614174000",
            "owner": "john_doe",
            "tags": ["expense", "2024"],
            "fileName": "123e4567-e89b-12d3-a456-426614174000_receipt.jpg",
            "uploadedAt": "2024-06-14T10:30:00Z"
        }
    ]
    ```
    """)
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Document Management" }];
    
    var ownerParam = operation.Parameters.FirstOrDefault(p => p.Name == "owner");
    if (ownerParam != null)
    {
        ownerParam.Description = "Username or identifier of the document owner";
        ownerParam.Required = true;
        ownerParam.Example = new Microsoft.OpenApi.Any.OpenApiString("john_doe");
    }
    
    return operation;
})
.Produces<IEnumerable<DocumentMeta>>(StatusCodes.Status200OK, "application/json")
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
.WithSummary("Get document metadata by ID")
.WithDescription("""
    Retrieves metadata for a specific document by its unique identifier.
    
    **Key Features:**
    - Returns complete document metadata including tags and timestamps
    - Access control: only returns documents owned by the specified user
    - Returns 404 if document doesn't exist or user doesn't have access
    
    **Parameters:**
    - `id` (path, required): Unique GUID identifier of the document
    - `owner` (query, required): Username/identifier of the document owner
    
    **Use Cases:**
    - Verify document exists before performing operations
    - Get current metadata before updates
    - Display document information in UI
    
    **Example:**
    GET /documents/123e4567-e89b-12d3-a456-426614174000?owner=john_doe
    """)
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Document Management" }];
    
    var idParam = operation.Parameters.FirstOrDefault(p => p.Name == "id");
    if (idParam != null)
    {
        idParam.Description = "Unique GUID identifier of the document";
        idParam.Example = new Microsoft.OpenApi.Any.OpenApiString("123e4567-e89b-12d3-a456-426614174000");
    }
    
    var ownerParam = operation.Parameters.FirstOrDefault(p => p.Name == "owner");
    if (ownerParam != null)
    {
        ownerParam.Description = "Username or identifier of the document owner";
        ownerParam.Required = true;
        ownerParam.Example = new Microsoft.OpenApi.Any.OpenApiString("john_doe");
    }
    
    return operation;
})
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
.WithSummary("Update document tags")
.WithDescription("""
    Updates the tags associated with a specific document.
    
    **Key Features:**
    - Replace all existing tags with the provided list
    - Empty array removes all tags from the document
    - Access control: only updates documents owned by the specified user
    - Returns updated document metadata
    
    **Parameters:**
    - `id` (path, required): Unique GUID identifier of the document
    - `owner` (query, required): Username/identifier of the document owner
    - `tags` (body, required): Array of tag strings to assign to the document
    
    **Request Body:**
    ```json
    ["expense", "2024", "restaurant", "business"]
    ```
    
    **Use Cases:**
    - Add new tags to categorize documents
    - Remove outdated or incorrect tags
    - Reorganize document classification system
    
    **Note:** This operation replaces ALL existing tags. To add tags while preserving existing ones, 
    first retrieve the current tags via GET /documents/{id}, then include both old and new tags in the update.
    """)
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Document Management" }];
    
    var idParam = operation.Parameters.FirstOrDefault(p => p.Name == "id");
    if (idParam != null)
    {
        idParam.Description = "Unique GUID identifier of the document to update";
        idParam.Example = new Microsoft.OpenApi.Any.OpenApiString("123e4567-e89b-12d3-a456-426614174000");
    }
    
    var ownerParam = operation.Parameters.FirstOrDefault(p => p.Name == "owner");
    if (ownerParam != null)
    {
        ownerParam.Description = "Username or identifier of the document owner";
        ownerParam.Required = true;
        ownerParam.Example = new Microsoft.OpenApi.Any.OpenApiString("john_doe");
    }
    
    if (operation.RequestBody?.Content?.ContainsKey("application/json") == true)
    {
        operation.RequestBody.Description = "Array of tag strings to assign to the document (replaces all existing tags)";
    }
    
    return operation;
})
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
.WithSummary("Delete a document")
.WithDescription("""
    Permanently deletes a document and its associated file from the storage system.
    
    **Key Features:**
    - Completely removes both metadata and physical file
    - Access control: only deletes documents owned by the specified user
    - Irreversible operation - no recovery possible
    - Returns 404 if document doesn't exist or user doesn't have access
    
    **Parameters:**
    - `id` (path, required): Unique GUID identifier of the document to delete
    - `owner` (query, required): Username/identifier of the document owner
    
    **⚠️ Important Warning:**
    This operation is irreversible. The document file and all metadata will be permanently removed 
    from the system. Make sure you have backups if needed.
    
    **Use Cases:**
    - Remove outdated or incorrect documents
    - Clean up storage space
    - Comply with data retention policies
    - Remove sensitive documents
    
    **Example:**
    DELETE /documents/123e4567-e89b-12d3-a456-426614174000?owner=john_doe
    """)
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Document Management" }];
    
    var idParam = operation.Parameters.FirstOrDefault(p => p.Name == "id");
    if (idParam != null)
    {
        idParam.Description = "Unique GUID identifier of the document to delete";
        idParam.Example = new Microsoft.OpenApi.Any.OpenApiString("123e4567-e89b-12d3-a456-426614174000");
    }
    
    var ownerParam = operation.Parameters.FirstOrDefault(p => p.Name == "owner");
    if (ownerParam != null)
    {
        ownerParam.Description = "Username or identifier of the document owner";
        ownerParam.Required = true;
        ownerParam.Example = new Microsoft.OpenApi.Any.OpenApiString("john_doe");
    }
    
    return operation;
})
.Produces(StatusCodes.Status204NoContent)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status500InternalServerError);

// Document verification endpoint for mortgage approval
app.MapGet("/documents/user/{owner}/verification", async (string owner, IDocumentService documentService) =>
{
    try
    {
        var documents = await documentService.GetDocumentsAsync(owner);
        
        // Check for required document types by tags
        var hasIncomeDocuments = documents.Any(d => d.Tags.Any(t => 
            t.Contains("income", StringComparison.OrdinalIgnoreCase) || 
            t.Contains("salary", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("pay", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("w2", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("paystub", StringComparison.OrdinalIgnoreCase)));
            
        var hasCreditReport = documents.Any(d => d.Tags.Any(t => 
            t.Contains("credit", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("credit-report", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("creditreport", StringComparison.OrdinalIgnoreCase)));
            
        var hasEmploymentVerification = documents.Any(d => d.Tags.Any(t => 
            t.Contains("employment", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("employer", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("employment-verification", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("verification", StringComparison.OrdinalIgnoreCase)));
            
        var hasPropertyAppraisal = documents.Any(d => d.Tags.Any(t => 
            t.Contains("appraisal", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("property", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("valuation", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("property-appraisal", StringComparison.OrdinalIgnoreCase)));

        // Create verification result
        var result = new
        {
            UserName = owner,
            Documents = documents.Select(d => new
            {
                DocumentType = string.Join(", ", d.Tags),
                FileName = d.FileName,
                IsVerified = true, // Assume uploaded documents are verified
                UploadedAt = d.UploadedAt,
                VerifiedAt = d.UploadedAt
            }).ToList(),
            HasIncomeDocuments = hasIncomeDocuments,
            HasCreditReport = hasCreditReport,
            HasEmploymentVerification = hasEmploymentVerification,
            HasPropertyAppraisal = hasPropertyAppraisal,
            AllDocumentsVerified = hasIncomeDocuments && hasCreditReport && hasEmploymentVerification && hasPropertyAppraisal
        };
        
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error verifying documents for user {Owner}", owner);
        return Results.Problem("An error occurred while verifying documents");
    }
})
.WithName("VerifyDocuments")
.WithSummary("Verify mortgage documents for a user")
.WithDescription("""
    Verifies that a user has uploaded all required documents for mortgage approval.
    
    **Required Document Types (identified by tags):**
    - **Income Documents**: Tags containing 'income', 'salary', 'pay', 'w2', 'paystub'
    - **Credit Report**: Tags containing 'credit', 'credit-report', 'creditreport'
    - **Employment Verification**: Tags containing 'employment', 'employer', 'employment-verification', 'verification'
    - **Property Appraisal**: Tags containing 'appraisal', 'property', 'valuation', 'property-appraisal'
    
    **Parameters:**
    - `owner` (required): Username/identifier to verify documents for
    
    **Response:**
    Returns verification status with detailed breakdown of each document type.
    """)
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Document Verification" }];
    return operation;
})
.Produces(StatusCodes.Status200OK)
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

// Data models with OpenAPI annotations
/// <summary>
/// Document metadata containing all information about an uploaded document
/// </summary>
/// <param name="Id">Unique identifier for the document (auto-generated GUID)</param>
/// <param name="Owner">Username or identifier of the document owner</param>
/// <param name="Tags">List of tags for document organization and categorization</param>
/// <param name="FileName">Original filename with GUID prefix for uniqueness</param>
/// <param name="UploadedAt">Timestamp when the document was uploaded (UTC)</param>
public record DocumentMeta(
    [property: Description("Unique identifier for the document")] Guid Id,
    [property: Description("Username or identifier of the document owner")] string Owner,
    [property: Description("List of tags for document organization")] List<string> Tags,
    [property: Description("Filename with unique GUID prefix")] string FileName,
    [property: Description("Upload timestamp in UTC")] DateTimeOffset UploadedAt
);

// Service interface definition is in DocumentModels.cs
