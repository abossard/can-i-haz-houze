// Temporary suppression of nullable return and async warnings until proper nullability annotations are added
#pragma warning disable CS8603 // Possible null reference return
#pragma warning disable CS1998 // Async method lacks 'await'
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using System.Text.RegularExpressions;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel;
using CanIHazHouze.DocumentService;
using Microsoft.OpenApi.Models;
using System.Runtime.InteropServices;

var builder = WebApplication.CreateBuilder(args);

// Enhanced startup logging
var logger = LoggerFactory.Create(config => config.AddConsole()).CreateLogger("Startup");
logger.LogInformation("🔧 Building CanIHazHouze.DocumentService...");

// Add service defaults & Aspire client integrations.
logger.LogInformation("➕ Adding Aspire service defaults...");
builder.AddServiceDefaults();
builder.AddMCPSupport();

// Add services to the container.
logger.LogInformation("➕ Adding problem details support...");
builder.Services.AddProblemDetails();

// Add CORS
logger.LogInformation("➕ Configuring CORS policy...");
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
logger.LogInformation("➕ Adding OpenAPI with Azure Container Apps servers...");
builder.AddOpenApiWithAzureContainerAppsServers();

// Configure document storage options (now for blob storage)
logger.LogInformation("⚙️  Configuring document storage options...");
builder.Services.Configure<CanIHazHouze.DocumentService.DocumentStorageOptions>(
    builder.Configuration.GetSection("DocumentStorage"));

// Add Azure Cosmos DB using Aspire
logger.LogInformation("🌐 Adding Azure Cosmos DB client...");
builder.AddAzureCosmosClient("cosmos");

// Add Azure Blob Storage using Aspire
logger.LogInformation("📁 Adding Azure Blob Storage client...");
builder.AddAzureBlobClient("blobs");

// Keyless Azure OpenAI client (DefaultAzureCredential)
// REQUIRED: Set user secret with: dotnet user-secrets set "ConnectionStrings:openai" "Endpoint=https://YOUR-RESOURCE.openai.azure.com/"
logger.LogInformation("🤖 Configuring Azure OpenAI client (DefaultAzureCredential)...");
var openAiConn = builder.Configuration.GetConnectionString("openai");
if (string.IsNullOrWhiteSpace(openAiConn))
{
    throw new InvalidOperationException(
        "OpenAI connection string is required. Set user secret with: " +
        "dotnet user-secrets set \"ConnectionStrings:openai\" \"Endpoint=https://YOUR-RESOURCE.openai.azure.com/\"");
}

string? openAiEndpoint = openAiConn.Split(';', StringSplitOptions.RemoveEmptyEntries)
    .FirstOrDefault(p => p.StartsWith("Endpoint=", StringComparison.OrdinalIgnoreCase))?
    .Substring("Endpoint=".Length);

if (string.IsNullOrWhiteSpace(openAiEndpoint) || !Uri.TryCreate(openAiEndpoint, UriKind.Absolute, out var openAiUri) || openAiUri.Scheme != Uri.UriSchemeHttps)
{
    throw new InvalidOperationException(
        $"Invalid OpenAI endpoint: '{openAiEndpoint}'. Must be a valid HTTPS URL.");
}

builder.Services.AddSingleton(sp =>
{
    var credential = new Azure.Identity.DefaultAzureCredential();
    return new Azure.AI.OpenAI.AzureOpenAIClient(openAiUri, credential);
});

// Add document service
logger.LogInformation("📄 Registering document service...");
builder.Services.AddScoped<IDocumentService, DocumentServiceImpl>();

// Add AI document analysis service
logger.LogInformation("🧠 Registering AI document analysis service (Azure OpenAI backed)...");
builder.Services.AddScoped<IDocumentAIService, DocumentAIService>();

logger.LogInformation("✅ Service configuration completed");
logger.LogInformation("🏗️  Building application...");

var app = builder.Build();

// Enhanced startup logging
app.Logger.LogInformation("🚀 CanIHazHouze.DocumentService starting up...");
app.Logger.LogInformation("📋 Application Name: {ApplicationName}", builder.Environment.ApplicationName);
app.Logger.LogInformation("🌍 Environment: {Environment}", builder.Environment.EnvironmentName);
app.Logger.LogInformation("📁 Content Root: {ContentRoot}", builder.Environment.ContentRootPath);
app.Logger.LogInformation("🖥️  Platform: {Platform}", Environment.OSVersion.Platform);

// Configure application lifetime events
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStarted.Register(() =>
{
    app.Logger.LogInformation("✅ CanIHazHouze.DocumentService has started successfully");
    app.Logger.LogInformation("🌐 Application is now ready to accept requests");
});

lifetime.ApplicationStopping.Register(() =>
{
    app.Logger.LogWarning("⚠️  CanIHazHouze.DocumentService is stopping...");
    app.Logger.LogInformation("🔄 Graceful shutdown initiated");
});

lifetime.ApplicationStopped.Register(() =>
{
    app.Logger.LogInformation("🛑 CanIHazHouze.DocumentService has stopped completely");
    app.Logger.LogInformation("👋 Goodbye!");
});

// Configure Unix signal handling (ignore signals but log them)
ConfigureSignalHandling(app.Logger);

// Configure the HTTP request pipeline.
app.Logger.LogInformation("🔧 Configuring HTTP request pipeline...");

app.Logger.LogInformation("➕ Adding exception handler...");
app.UseExceptionHandler();

// Use CORS
app.Logger.LogInformation("➕ Adding CORS middleware...");
app.UseCors();

app.Logger.LogInformation("➕ Mapping OpenAPI endpoints...");
app.MapOpenApi();
app.MapScalarApiReference();

app.Logger.LogInformation("➕ Adding static files middleware...");
app.UseStaticFiles(); // optional for serving files

app.Logger.LogInformation("🔗 Configuring API endpoints...");
app.Logger.LogInformation("📄 Setting up document management endpoints...");

// Base64 document upload endpoint
app.MapPost("/documents/base64", async (
    [FromBody] Base64DocumentUploadRequest request,
    IDocumentService documentService,
    IDocumentAIService aiService) =>
{
    try
    {
        // Validate inputs
        if (string.IsNullOrWhiteSpace(request.Owner))
        {
            return Results.BadRequest("Owner parameter is required");
        }
        
        if (string.IsNullOrWhiteSpace(request.Base64Content))
        {
            return Results.BadRequest("Base64Content is required");
        }
        
        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            return Results.BadRequest("FileName is required");
        }

        app.Logger.LogInformation("Starting Base64 document upload for owner: {Owner}, file: {FileName}", 
            request.Owner, request.FileName);
        
        // Decode Base64 content
        byte[] fileBytes;
        try
        {
            fileBytes = Convert.FromBase64String(request.Base64Content);
        }
        catch (FormatException)
        {
            return Results.BadRequest("Invalid Base64 content format");
        }
        
        if (fileBytes.Length == 0)
        {
            return Results.BadRequest("Decoded file content is empty");
        }        // Create IFormFile from Base64 content
        var stream = new MemoryStream(fileBytes);
        var formFile = new FormFile(stream, 0, fileBytes.Length, "file", request.FileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = GetContentTypeFromFileName(request.FileName)
        };
        
        // Local helper function for content type detection
        static string GetContentTypeFromFileName(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            return extension switch
            {
                ".pdf" => "application/pdf",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".txt" => "text/plain",
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".zip" => "application/zip",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xls" => "application/vnd.ms-excel",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".md" => "text/markdown",
                _ => "application/octet-stream"
            };
        }
        
        var tagList = request.Tags ?? new List<string>();
        
        // If AI tag suggestions are requested, prepare content for analysis
        string? textForAnalysis = null;
        if (request.SuggestTags)
        {
            var extension = Path.GetExtension(request.FileName).ToLowerInvariant();
            if (extension == ".txt" || extension == ".md")
            {
                try
                {
                    textForAnalysis = System.Text.Encoding.UTF8.GetString(fileBytes);
                    app.Logger.LogInformation("Read {Length} characters from Base64 text file for AI analysis", 
                        textForAnalysis.Length);
                }
                catch (Exception ex)
                {
                    app.Logger.LogWarning(ex, "Failed to decode text content for AI analysis, will use filename only");
                    textForAnalysis = $"Filename: {request.FileName}\nExisting tags: {string.Join(", ", tagList)}";
                }
            }
            else
            {
                // For non-text files, use filename and existing tags
                textForAnalysis = $"Filename: {request.FileName}\nExisting tags: {string.Join(", ", tagList)}";
            }
        }
        
        var documentMeta = await documentService.UploadDocumentAsync(request.Owner, formFile, tagList);
        
        app.Logger.LogInformation("Base64 document uploaded successfully: {DocumentId}", documentMeta.Id);
        
        // If AI tag suggestions are requested, generate them using prepared content
        List<string>? suggestedTags = null;
        if (request.SuggestTags && !string.IsNullOrEmpty(textForAnalysis))
        {
            try
            {
                app.Logger.LogInformation("Generating AI tag suggestions for Base64 document: {DocumentId}", 
                    documentMeta.Id);
                
                suggestedTags = await aiService.SuggestTagsAsync(textForAnalysis, tagList, request.MaxSuggestions);
                app.Logger.LogInformation("Generated {Count} AI tag suggestions", suggestedTags?.Count ?? 0);
            }
            catch (Exception ex)
            {
                app.Logger.LogWarning(ex, "Failed to generate AI tag suggestions for Base64 document {DocumentId}", 
                    documentMeta.Id);
                // Continue without suggestions rather than failing the entire upload
            }
        }

        var response = new
        {
            Document = documentMeta,
            AITagSuggestions = suggestedTags,
            SuggestionsGenerated = suggestedTags?.Any() == true,
            Message = suggestedTags?.Any() == true 
                ? "Base64 document uploaded successfully with AI tag suggestions" 
                : "Base64 document uploaded successfully"
        };
        
        return Results.Created($"/documents/{documentMeta.Id}?owner={request.Owner}", response);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ex.Message);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error uploading Base64 document for owner {Owner}", request.Owner);
        return Results.Problem("An error occurred while uploading the Base64 document");
    }
})
.WithName("UploadBaseSixtyFourDocument")
.WithSummary("Upload a document using Base64 encoding")
.WithDescription("""
    Uploads a new document using Base64-encoded content instead of multipart form data.
    
    **Key Features:**
    - Accepts Base64-encoded file content in JSON request body
    - Supports all file formats (images, PDFs, documents, etc.)
    - Configurable file size limits (applied to decoded content)
    - Automatic GUID-based unique filename generation
    - Tag-based organization system
    - Per-user document isolation
    - Optional AI-powered tag suggestions during upload
    
    **Request Body:**
    ```json
    {
        "owner": "john_doe",
        "fileName": "report.pdf",
        "base64Content": "JVBERi0xLjQKJcOkw7zDssOdw6jDr...",
        "tags": ["expense", "2024"],
        "suggestTags": true,
        "maxSuggestions": 5
    }
    ```
    
    **Use Cases:**
    - Mobile app uploads where multipart forms are complex
    - JavaScript/SPA applications with direct file-to-Base64 conversion
    - API integrations that prefer JSON-only communication
    - Systems where binary data handling is restricted
    
    **Response:**
    Returns the complete document metadata and optionally AI-suggested tags for user review.
    """)
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Document Management" }];
    operation.RequestBody.Description = "Base64 document upload request with encoded file content";
    return operation;
})
.Accepts<Base64DocumentUploadRequest>("application/json")
.Produces<DocumentMeta>(StatusCodes.Status201Created)
.Produces<string>(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status500InternalServerError);

// OpenAPI-tagged CRUD endpoints
app.MapPost("/documents", async (
    [FromForm, Required] string owner,
    IFormFile file,
    [FromForm] string? tags,
    IDocumentService documentService,
    IDocumentAIService aiService,
    [FromQuery] bool suggestTags = false,
    [FromQuery] int maxSuggestions = 3) =>
{    try
    {
        // Validate inputs
        if (string.IsNullOrWhiteSpace(owner))
        {
            return Results.BadRequest("Owner parameter is required");
        }
        
        if (file == null)
        {
            return Results.BadRequest("File is required");
        }
          app.Logger.LogInformation("Starting document upload for owner: {Owner}, file: {FileName}", owner, file.FileName);
        
        var tagList = string.IsNullOrWhiteSpace(tags) 
            ? new List<string>() 
            : tags.Split(',').Select(t => t.Trim()).ToList();
        
        // If AI tag suggestions are requested, read file content first (before uploading)
        string? textForAnalysis = null;
        if (suggestTags)
        {
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension == ".txt" || extension == ".md")
            {
                try
                {
                    using var reader = new StreamReader(file.OpenReadStream());
                    textForAnalysis = await reader.ReadToEndAsync();
                    app.Logger.LogInformation("Read {Length} characters from text file for AI analysis", textForAnalysis.Length);
                }
                catch (Exception ex)
                {
                    app.Logger.LogWarning(ex, "Failed to read file content for AI analysis, will use filename only");
                    textForAnalysis = $"Filename: {file.FileName}\nExisting tags: {string.Join(", ", tagList)}";
                }
            }
            else
            {
                // For non-text files, use filename and existing tags
                textForAnalysis = $"Filename: {file.FileName}\nExisting tags: {string.Join(", ", tagList)}";
            }
        }
        
        var documentMeta = await documentService.UploadDocumentAsync(owner, file, tagList);
        
        app.Logger.LogInformation("Document uploaded successfully: {DocumentId}", documentMeta.Id);
          // If AI tag suggestions are requested, generate them using pre-read content
        List<string>? suggestedTags = null;
        if (suggestTags && !string.IsNullOrEmpty(textForAnalysis))
        {
            try
            {
                app.Logger.LogInformation("Generating AI tag suggestions for document: {DocumentId}", documentMeta.Id);
                
                suggestedTags = await aiService.SuggestTagsAsync(textForAnalysis, tagList, maxSuggestions);
                app.Logger.LogInformation("Generated {Count} AI tag suggestions", suggestedTags?.Count ?? 0);
            }
            catch (Exception ex)
            {
                app.Logger.LogWarning(ex, "Failed to generate AI tag suggestions for document {DocumentId}", documentMeta.Id);
                // Continue without suggestions rather than failing the entire upload
            }
        }

        var response = new
        {
            Document = documentMeta,
            AITagSuggestions = suggestedTags,
            SuggestionsGenerated = suggestedTags?.Any() == true,
            Message = suggestedTags?.Any() == true 
                ? "Document uploaded successfully with AI tag suggestions" 
                : "Document uploaded successfully"
        };
        
        return Results.Created($"/documents/{documentMeta.Id}?owner={owner}", response);
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
    Uploads a new document to the storage system with optional metadata tags and AI tag suggestions.
    
    **Key Features:**
    - Supports all file formats (images, PDFs, documents, etc.)
    - Configurable file size limits (default: 100MB)
    - Automatic GUID-based unique filename generation
    - Tag-based organization system
    - Per-user document isolation
    - Optional AI-powered tag suggestions during upload
    
    **Form Data Parameters:**
    - `owner` (required): Username/identifier for document ownership
    - `file` (required): The file to upload (multipart/form-data)
    - `tags` (optional): Comma-separated list of tags for organization
    
    **Query Parameters:**
    - `suggestTags` (optional, default: false): Generate AI tag suggestions after upload
    - `maxSuggestions` (optional, default: 3): Maximum number of AI tag suggestions to generate
    
    **Examples:**
    - Basic upload: owner="john_doe", file=report.pdf
    - With tags: owner="john_doe", file=receipt.jpg, tags="expense, 2024, restaurant"
    - With AI suggestions: owner="john_doe", file=report.pdf, suggestTags=true, maxSuggestions=5
    
    **Response:**
    Returns the complete document metadata and optionally AI-suggested tags for user review.
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
    
    var suggestTagsParam = operation.Parameters.FirstOrDefault(p => p.Name == "suggestTags");
    if (suggestTagsParam != null)
    {
        suggestTagsParam.Description = "Whether to generate AI tag suggestions after upload. Suggestions are returned for user review.";
        suggestTagsParam.Example = new Microsoft.OpenApi.Any.OpenApiBoolean(false);
    }
    
    var maxSuggestionsParam = operation.Parameters.FirstOrDefault(p => p.Name == "maxSuggestions");
    if (maxSuggestionsParam != null)
    {
        maxSuggestionsParam.Description = "Maximum number of AI tag suggestions to generate (only used when suggestTags=true).";
        maxSuggestionsParam.Example = new Microsoft.OpenApi.Any.OpenApiInteger(3);
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

// Document download endpoint
app.MapGet("/documents/{id}/download", async (Guid id, [Required] string owner, IDocumentService documentService) =>
{
    try
    {
        // Get document metadata first to verify ownership and get filename
        var document = await documentService.GetDocumentAsync(id, owner);
        if (document == null)
        {
            return Results.NotFound("Document not found or access denied");
        }
        
        // Get document content stream
        var contentStream = await documentService.GetDocumentContentAsync(id, owner);
        if (contentStream == null)
        {
            return Results.NotFound("Document content not found");
        }
        
        // Determine content type from filename
        var extension = Path.GetExtension(document.FileName).ToLowerInvariant();
        var contentType = extension switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".txt" => "text/plain",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".zip" => "application/zip",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream"
        };
        
        // Extract original filename without GUID prefix
        var originalFileName = document.FileName;
        if (originalFileName.Contains('_') && Guid.TryParse(originalFileName.Split('_')[0], out _))
        {
            originalFileName = string.Join("_", originalFileName.Split('_').Skip(1));
        }
        
        return Results.Stream(contentStream, contentType, originalFileName);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error downloading document: {ex.Message}", statusCode: 500);
    }
})
.WithName("DownloadDocument")
.WithSummary("Download a document file")
.WithDescription("""
    Downloads the actual file content for a specific document.
    
    **Key Features:**
    - Returns the original file with appropriate content-type headers
    - Access control: only downloads documents owned by the specified user
    - Preserves original filename for download
    - Supports all file types uploaded to the system
    
    **Parameters:**
    - `id` (path, required): Unique GUID identifier of the document
    - `owner` (query, required): Username/identifier of the document owner
    
    **Response:**
    Returns the file content as a stream with appropriate headers for browser download.
    
    **Use Cases:**
    - Download documents for viewing or editing
    - Backup or archive documents locally
    - Share document files with external systems
    
    **Example:**
    GET /documents/123e4567-e89b-12d3-a456-426614174000/download?owner=john_doe
    """)
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Document Management" }];
    
    var idParam = operation.Parameters.FirstOrDefault(p => p.Name == "id");
    if (idParam != null)
    {
        idParam.Description = "Unique GUID identifier of the document to download";
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
.Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
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

// AI-powered document analysis endpoint
app.MapPost("/documents/{id}/analyze", async (
    Guid id, 
    [Required] string owner, 
    IDocumentService documentService,
    IDocumentAIService aiService) =>
{
    try
    {
        app.Logger.LogInformation("Starting AI analysis for document {DocumentId} owned by {Owner}", id, owner);
        
        // First, get the document to verify it exists and user has access
        var document = await documentService.GetDocumentAsync(id, owner);
        if (document is null)
        {
            return Results.NotFound("Document not found or access denied");
        }        // Read the document content from blob storage
        var contentStream = await documentService.GetDocumentContentAsync(id, owner);
        if (contentStream == null)
        {
            return Results.NotFound("Document content not found");
        }

        // For demonstration, we'll read text files directly
        // In production, you'd use OCR for images, PDF readers for PDFs, etc.
        string textContent;
        var extension = Path.GetExtension(document.FileName).ToLowerInvariant();
        
        if (extension == ".txt" || extension == ".md")
        {
            using var reader = new StreamReader(contentStream);
            textContent = await reader.ReadToEndAsync();
        }
        else
        {
            // For non-text files, we'll use a placeholder
            // In production, you'd implement proper text extraction
            textContent = $"Document file: {document.FileName}\nFile type: {extension}\nUploaded: {document.UploadedAt}\nTags: {string.Join(", ", document.Tags)}";
        }

        // Perform AI analysis
        var metadata = await aiService.ExtractMetadataAsync(textContent, document.FileName);
        
        var result = new
        {
            DocumentId = id,
            FileName = document.FileName,
            OriginalTags = document.Tags,
            AIAnalysis = metadata,
            AnalyzedAt = DateTimeOffset.UtcNow,
            TextContentLength = textContent.Length,
            SupportedFileType = extension == ".txt" || extension == ".md"
        };

        app.Logger.LogInformation("Successfully completed AI analysis for document {DocumentId}", id);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error analyzing document {DocumentId} for owner {Owner}", id, owner);
        return Results.Problem("An error occurred while analyzing the document");
    }
})
.WithName("AnalyzeDocument")
.WithSummary("Analyze a document using AI to extract metadata and insights")
.WithDescription("""
    Uses Azure OpenAI to analyze a document and extract structured metadata including:
    
    **Key Features:**
    - Document type classification (Invoice, Contract, Receipt, etc.)
    - Automatic summary generation
    - Entity extraction (names, companies, amounts, dates)
    - Tag suggestions based on content
    - Confidence scoring for analysis quality
    
    **Supported File Types:**
    - Text files (.txt, .md) - full content analysis
    - Other files - metadata-based analysis (filename, tags, etc.)
    
    **Parameters:**
    - `id` (required): Document ID to analyze
    - `owner` (required): Username/identifier of the document owner
    
    **Response:**
    Returns detailed AI analysis including document type, summary, extracted entities, suggested tags, and confidence scores.
    
    **Note:** This operation requires an active Azure OpenAI connection and may take a few seconds to complete.
    """)
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "AI Document Analysis" }];
    
    var idParam = operation.Parameters.FirstOrDefault(p => p.Name == "id");
    if (idParam != null)
    {
        idParam.Description = "Unique identifier of the document to analyze";
        idParam.Required = true;
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
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status500InternalServerError);

// Semi-automatic tag enhancement endpoint
app.MapPut("/documents/{id}/enhance-tags", async (
    Guid id, 
    [Required] string owner, 
    [FromBody] EnhanceTagsRequest? request,
    IDocumentService documentService,
    IDocumentAIService aiService) =>
{
    try
    {
        app.Logger.LogInformation("Starting tag enhancement for document {DocumentId} owned by {Owner}", id, owner);
        
        // First, get the document to verify it exists and user has access
        var document = await documentService.GetDocumentAsync(id, owner);
        if (document is null)
        {
            return Results.NotFound("Document not found or access denied");
        }        // Read the document content for AI analysis from blob storage
        var contentStream = await documentService.GetDocumentContentAsync(id, owner);
        if (contentStream == null)
        {
            return Results.NotFound("Document content not found");
        }

        // Extract text content for analysis
        string textContent;
        var extension = Path.GetExtension(document.FileName).ToLowerInvariant();
        
        if (extension == ".txt" || extension == ".md")
        {
            using var reader = new StreamReader(contentStream);
            textContent = await reader.ReadToEndAsync();
        }
        else
        {
            // For non-text files, use metadata for analysis
            contentStream.Dispose(); // Clean up the stream since we're not using it
            // For non-text files, use metadata for analysis
            textContent = $"Document file: {document.FileName}\nFile type: {extension}\nUploaded: {document.UploadedAt}\nExisting tags: {string.Join(", ", document.Tags)}";
        }

        // Get AI-suggested tags
        var suggestedTags = await aiService.SuggestTagsAsync(
            textContent, 
            document.Tags, 
            request?.MaxSuggestions ?? 5);

        // Merge with existing tags (avoiding duplicates)
        var enhancedTags = document.Tags.ToList();
        var tagsToAdd = new List<string>();
        
        foreach (var suggestedTag in suggestedTags)
        {
            if (!enhancedTags.Any(existing => 
                string.Equals(existing, suggestedTag, StringComparison.OrdinalIgnoreCase)))
            {
                if (request?.AutoApply == true)
                {
                    enhancedTags.Add(suggestedTag);
                }
                tagsToAdd.Add(suggestedTag);
            }
        }        CanIHazHouze.DocumentService.DocumentMeta? updatedDocument = null;
        if (request?.AutoApply == true && tagsToAdd.Any())
        {
            // Automatically apply the suggested tags
            updatedDocument = await documentService.UpdateDocumentTagsAsync(id, owner, enhancedTags);
        }

        var result = new
        {
            DocumentId = id,
            FileName = document.FileName,
            OriginalTags = document.Tags,
            SuggestedTags = tagsToAdd,
            EnhancedTags = enhancedTags,
            AutoApplied = request?.AutoApply == true,
            UpdatedDocument = updatedDocument,
            EnhancedAt = DateTimeOffset.UtcNow,
            TotalTagsAfterEnhancement = enhancedTags.Count
        };

        app.Logger.LogInformation("Successfully enhanced tags for document {DocumentId}. Added {NewTagsCount} new tags", 
            id, tagsToAdd.Count);
        
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error enhancing tags for document {DocumentId} owned by {Owner}", id, owner);
        return Results.Problem("An error occurred while enhancing document tags");
    }
})
.WithName("EnhanceDocumentTags")
.WithSummary("Enhance document tags with AI suggestions")
.WithDescription("""
    Analyzes an existing document and suggests additional tags to improve organization and discoverability.
    
    **Key Features:**
    - Preserves all existing user tags
    - Suggests relevant additional tags based on document content
    - Option to auto-apply suggestions or return them for user review
    - Avoids duplicate tags (case-insensitive comparison)
    - Works with text files (full content analysis) and other files (metadata analysis)
    
    **Parameters:**
    - `id` (required): Document ID to enhance
    - `owner` (required): Username/identifier of the document owner
    
    **Request Body (optional):**
    ```json
    {
        "autoApply": false,
        "maxSuggestions": 5
    }
    ```
    
    **Response:**
    Returns original tags, suggested tags, and optionally the updated document if auto-applied.
    
    **Use Cases:**
    - Improve document organization for existing uploads
    - Standardize tagging across document collections
    - Discover new categorization opportunities
    """)
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "AI Document Analysis" }];
    
    var idParam = operation.Parameters.FirstOrDefault(p => p.Name == "id");
    if (idParam != null)
    {
        idParam.Description = "Unique identifier of the document to enhance";
        idParam.Required = true;
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
.Accepts<EnhanceTagsRequest>("application/json")
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status500InternalServerError);

app.MapDefaultEndpoints();

// TODO: MCP tool registration needs migration to official SDK
// The official SDK requires using [McpServerToolType] and [McpServerTool] attributes
// or registering tools via builder.Services.AddMcpServer().WithTools<TToolsClass>()
// For now, tools are exposed via REST API endpoints above and can be called directly via HTTP
/*
var mcpServer = app.Services.GetRequiredService<McpServer>();
var serviceProvider = app.Services;

// Register upload document tool
mcpServer.RegisterTool<UploadDocumentMCPRequest>("upload_document",
    "Upload a new document with optional tags and AI suggestions",
    async req => 
    {
        using var scope = serviceProvider.CreateScope();
        var documentService = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        var aiService = scope.ServiceProvider.GetRequiredService<IDocumentAIService>();
        
        // Convert base64 to stream for upload
        var fileBytes = Convert.FromBase64String(req.Base64Content);
        var stream = new MemoryStream(fileBytes);
        var formFile = new FormFile(stream, 0, fileBytes.Length, "file", req.FileName);
        
        var result = await documentService.UploadDocumentAsync(req.Owner, formFile, req.Tags ?? new List<string>());
        
        // Add AI suggestions if requested
        List<string>? suggestions = null;
        if (req.SuggestTags)
        {
            var textContent = req.FileName; // Simplified for demo
            suggestions = await aiService.SuggestTagsAsync(textContent, req.Tags ?? new List<string>(), req.MaxSuggestions);
        }
        
        return new { Document = result, AITagSuggestions = suggestions };
    });

// Register list documents tool
mcpServer.RegisterTool<ListDocumentsRequest>("list_documents",
    "Get all documents for a user",
    async req => 
    {
        using var scope = serviceProvider.CreateScope();
        var documentService = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        return await documentService.GetDocumentsAsync(req.Owner);
    });

// Register get document tool
mcpServer.RegisterTool<GetDocumentRequest>("get_document",
    "Get document metadata by ID",
    async req => 
    {
        using var scope = serviceProvider.CreateScope();
        var documentService = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        return await documentService.GetDocumentAsync(req.Id, req.Owner);
    });

// Register update document tags tool
mcpServer.RegisterTool<UpdateDocumentTagsRequest>("update_document_tags",
    "Update document tags",
    async req => 
    {
        using var scope = serviceProvider.CreateScope();
        var documentService = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        return await documentService.UpdateDocumentTagsAsync(req.Id, req.Owner, req.Tags);
    });

// Register delete document tool
mcpServer.RegisterTool<DeleteDocumentRequest>("delete_document",
    "Delete a document",
    async req => 
    {
        using var scope = serviceProvider.CreateScope();
        var documentService = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        return await documentService.DeleteDocumentAsync(req.Id, req.Owner);
    });

// Register verify documents tool
mcpServer.RegisterTool<VerifyDocumentsRequest>("verify_mortgage_documents",
    "Verify that a user has uploaded all required mortgage documents",
    async req => 
    {
        using var scope = serviceProvider.CreateScope();
        var documentService = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        
        var documents = await documentService.GetDocumentsAsync(req.Owner);
        
        var hasIncomeDocuments = documents.Any(d => d.Tags.Any(t => 
            t.Contains("income", StringComparison.OrdinalIgnoreCase) || 
            t.Contains("salary", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("pay", StringComparison.OrdinalIgnoreCase)));
            
        var hasCreditReport = documents.Any(d => d.Tags.Any(t => 
            t.Contains("credit", StringComparison.OrdinalIgnoreCase)));
            
        var hasEmploymentVerification = documents.Any(d => d.Tags.Any(t => 
            t.Contains("employment", StringComparison.OrdinalIgnoreCase)));
            
        var hasPropertyAppraisal = documents.Any(d => d.Tags.Any(t => 
            t.Contains("appraisal", StringComparison.OrdinalIgnoreCase)));

        return new
        {
            UserName = req.Owner,
            HasIncomeDocuments = hasIncomeDocuments,
            HasCreditReport = hasCreditReport,
            HasEmploymentVerification = hasEmploymentVerification,
            HasPropertyAppraisal = hasPropertyAppraisal,
            AllDocumentsVerified = hasIncomeDocuments && hasCreditReport && hasEmploymentVerification && hasPropertyAppraisal,
            TotalDocuments = documents.Count()
        };
    });

// Register analyze document tool
mcpServer.RegisterTool<AnalyzeDocumentRequest>("analyze_document_ai",
    "Analyze a document using AI to extract metadata and insights",
    async req => 
    {
        using var scope = serviceProvider.CreateScope();
        var documentService = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        
        var document = await documentService.GetDocumentAsync(req.Id, req.Owner);
        if (document == null) throw new InvalidOperationException("Document not found");
        
        var contentStream = await documentService.GetDocumentContentAsync(req.Id, req.Owner);
        if (contentStream == null) throw new InvalidOperationException("Document content not found");
        
        string textContent;
        var extension = Path.GetExtension(document.FileName).ToLowerInvariant();
        
        if (extension == ".txt" || extension == ".md")
        {
            using var reader = new StreamReader(contentStream);
            textContent = await reader.ReadToEndAsync();
        }
        else
        {
            textContent = $"Document file: {document.FileName}\nFile type: {extension}";
        }

        var aiService = scope.ServiceProvider.GetRequiredService<IDocumentAIService>();
        var metadata = await aiService.ExtractMetadataAsync(textContent, document.FileName);
        
        return new
        {
            DocumentId = req.Id,
            FileName = document.FileName,
            AIAnalysis = metadata,
            AnalyzedAt = DateTimeOffset.UtcNow
        };
    });

// Register MCP resources for DocumentService
mcpServer.RegisterResource("documents://all", "All Documents", 
    "Summary of all documents in the system",
    async () => new { message = "Document catalog resource - specify owner parameter for user documents" });

app.Logger.LogInformation("🔧 Registered MCP tools and resources for DocumentService");
*/

app.Logger.LogInformation("🎯 All endpoints configured successfully");
app.Logger.LogInformation("🚀 Starting CanIHazHouze.DocumentService...");
app.Logger.LogInformation("📍 The application will be available once Aspire dependencies are ready");

app.Run();

#pragma warning restore CS8603
#pragma warning restore CS1998

/// <summary>
/// Configures Unix signal handling to capture and log signals without terminating the application
/// </summary>
/// <param name="logger">Logger instance for signal logging</param>
static void ConfigureSignalHandling(ILogger logger)
{
    logger.LogInformation("🔧 Starting Unix signal handling configuration...");
    logger.LogInformation("🖥️  Platform detection: {Platform}", Environment.OSVersion.Platform);
    logger.LogInformation("⚙️  Operating System: {OS}", Environment.OSVersion);
    
    // Only configure signal handling on Unix platforms
    if (!OperatingSystem.IsWindows())
    {
        logger.LogInformation("🐧 Unix platform detected - Configuring signal handlers...");
        
        try
        {
            // Register handlers for common termination signals
            // Note: SIGKILL and SIGSTOP cannot be caught by design
            logger.LogInformation("📋 Preparing signal registration for Unix signals...");
            
            var signals = new[]
            {
                PosixSignal.SIGTERM,  // Termination request (kill command default)
                PosixSignal.SIGINT,   // Interrupt (Ctrl+C)
                PosixSignal.SIGHUP,   // Hang up (terminal closed)
                PosixSignal.SIGQUIT   // Quit (Ctrl+\)
            };

            logger.LogInformation("🎯 Registering handlers for {SignalCount} Unix signals: {Signals}", 
                signals.Length, string.Join(", ", signals.Select(s => s.ToString())));

            var registeredCount = 0;
            foreach (var signal in signals)
            {
                try
                {
                    logger.LogDebug("🔗 Registering handler for signal: {Signal}", signal);
                    
                    PosixSignalRegistration.Create(signal, context =>
                    {
                        var timestamp = DateTimeOffset.UtcNow;
                        logger.LogWarning("🚨 Unix signal {Signal} received at {Timestamp} - Signal ignored, application continues running", 
                            signal, timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff UTC"));
                        
                        // Cancel the default behavior (process termination) to keep the app running
                        context.Cancel = true;
                        
                        logger.LogInformation("✋ Signal {Signal} handling completed - Process termination cancelled", signal);
                    });
                    
                    registeredCount++;
                    logger.LogDebug("✅ Successfully registered handler for signal: {Signal}", signal);
                }
                catch (Exception signalEx)
                {
                    logger.LogWarning(signalEx, "⚠️  Failed to register handler for signal {Signal} - continuing with other signals", signal);
                }
            }

            logger.LogInformation("✅ Unix signal handling configured successfully");
            logger.LogInformation("📊 Signal registration summary: {RegisteredCount}/{TotalCount} signals registered", 
                registeredCount, signals.Length);
            logger.LogInformation("🛡️  Application will ignore SIGTERM, SIGINT, SIGHUP, and SIGQUIT signals");
            logger.LogInformation("ℹ️  Note: SIGKILL (kill -9) and SIGSTOP cannot be caught and will still terminate the process immediately");
            logger.LogInformation("🎮 Signal handling is now active - Use kill -9 <PID> to force terminate if needed");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Failed to configure Unix signal handling - Application will use default signal behavior");
            logger.LogError("💡 This may affect graceful shutdown behavior in containerized environments");
        }
    }
    else
    {
        logger.LogInformation("🪟 Windows platform detected - Unix signal handling not applicable");
        logger.LogInformation("ℹ️  Windows uses different termination mechanisms (Ctrl+C, Close button, etc.)");
        logger.LogInformation("✅ Signal handling configuration completed (Windows - no action required)");
    }
    
    logger.LogInformation("🏁 Unix signal handling configuration finished");
}

// Make Program class accessible for testing
namespace CanIHazHouze.DocumentService
{
    public partial class Program { }
}

// Data models with OpenAPI annotations
/// Request model for AI tag suggestions
/// </summary>
/// <param name="TextContent">Text content to analyze for tag suggestions (optional)</param>
/// <param name="FileName">Filename to analyze if text content not provided (optional)</param>
/// <param name="ExistingTags">Current tags to consider when suggesting new ones (optional)</param>
/// <param name="MaxTags">Maximum number of tags to suggest (default: 5)</param>
public record SuggestTagsRequest(
    [property: Description("Text content to analyze for tag suggestions")] string? TextContent = null,
    [property: Description("Filename to analyze for tag suggestions")] string? FileName = null,
    [property: Description("Existing tags to consider")] List<string>? ExistingTags = null,
    [property: Description("Maximum number of tags to suggest")] int MaxTags = 5
);

/// <summary>
/// Request model for enhancing document tags with AI suggestions
/// </summary>
/// <param name="AutoApply">Whether to automatically apply suggested tags to the document (default: false)</param>
/// <param name="MaxSuggestions">Maximum number of tag suggestions to generate (default: 5)</param>
public record EnhanceTagsRequest(
    [property: Description("Whether to automatically apply suggested tags to the document")] bool AutoApply = false,
    [property: Description("Maximum number of tag suggestions to generate")] int MaxSuggestions = 5
);

/// <summary>
/// Request model for Base64 document uploads
/// </summary>
/// <param name="Owner">Username or identifier of the document owner</param>
/// <param name="FileName">Original filename of the document</param>
/// <param name="Base64Content">Base64-encoded file content</param>
/// <param name="Tags">Optional list of tags for document organization</param>
/// <param name="SuggestTags">Whether to generate AI tag suggestions after upload (default: false)</param>
/// <param name="MaxSuggestions">Maximum number of AI tag suggestions to generate (default: 3)</param>
public record Base64DocumentUploadRequest(
    [property: Description("Username or identifier of the document owner")] string Owner,
    [property: Description("Original filename of the document")] string FileName,
    [property: Description("Base64-encoded file content")] string Base64Content,
    [property: Description("Optional list of tags for document organization")] List<string>? Tags = null,
    [property: Description("Whether to generate AI tag suggestions after upload")] bool SuggestTags = false,    [property: Description("Maximum number of AI tag suggestions to generate")] int MaxSuggestions = 3
);

// MCP Tool Request Models for DocumentService
/// <summary>
/// Request model for uploading document via MCP
/// </summary>
/// <param name="Owner">Username or identifier of the document owner</param>
/// <param name="FileName">Original filename of the document</param>
/// <param name="Base64Content">Base64-encoded file content</param>
/// <param name="Tags">Optional list of tags for document organization</param>
/// <param name="SuggestTags">Whether to generate AI tag suggestions after upload</param>
/// <param name="MaxSuggestions">Maximum number of AI tag suggestions to generate</param>
public record UploadDocumentMCPRequest(
    string Owner,
    string FileName,
    string Base64Content,
    List<string>? Tags = null,
    bool SuggestTags = false,
    int MaxSuggestions = 3
);

/// <summary>
/// Request model for listing documents via MCP
/// </summary>
/// <param name="Owner">Username or identifier of the document owner</param>
public record ListDocumentsRequest(string Owner);

/// <summary>
/// Request model for getting document metadata via MCP
/// </summary>
/// <param name="Id">Unique GUID identifier of the document</param>
/// <param name="Owner">Username or identifier of the document owner</param>
public record GetDocumentRequest(Guid Id, string Owner);

/// <summary>
/// Request model for updating document tags via MCP
/// </summary>
/// <param name="Id">Unique GUID identifier of the document</param>
/// <param name="Owner">Username or identifier of the document owner</param>
/// <param name="Tags">Array of tag strings to assign to the document</param>
public record UpdateDocumentTagsRequest(Guid Id, string Owner, List<string> Tags);

/// <summary>
/// Request model for deleting document via MCP
/// </summary>
/// <param name="Id">Unique GUID identifier of the document to delete</param>
/// <param name="Owner">Username or identifier of the document owner</param>
public record DeleteDocumentRequest(Guid Id, string Owner);

/// <summary>
/// Request model for verifying mortgage documents via MCP
/// </summary>
/// <param name="Owner">Username or identifier to verify documents for</param>
public record VerifyDocumentsRequest(string Owner);

/// <summary>
/// Request model for analyzing document via MCP
/// </summary>
/// <param name="Id">Unique identifier of the document to analyze</param>
/// <param name="Owner">Username or identifier of the document owner</param>
public record AnalyzeDocumentRequest(Guid Id, string Owner);

// Service interface definition is in DocumentModels.cs
