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
builder.Services.Configure<CanIHazHouze.DocumentService.DocumentStorageOptions>(
    builder.Configuration.GetSection("DocumentStorage"));

// Add Azure Cosmos DB using Aspire
builder.AddAzureCosmosClient("cosmos");

// Add Azure OpenAI client for document processing
builder.AddAzureOpenAIClient("openai");

// Add document service
builder.Services.AddScoped<IDocumentService, DocumentServiceImpl>();

// Add AI document analysis service
builder.Services.AddScoped<IDocumentAIService, DocumentAIService>();

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
        
        var documentMeta = await documentService.UploadDocumentAsync(owner, file, tagList);
        
        app.Logger.LogInformation("Document uploaded successfully: {DocumentId}", documentMeta.Id);
        
        // If AI tag suggestions are requested, generate them
        List<string>? suggestedTags = null;
        if (suggestTags)
        {
            try
            {
                app.Logger.LogInformation("Generating AI tag suggestions for document: {DocumentId}", documentMeta.Id);
                
                // For text files, read content for better suggestions
                string textForAnalysis;
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                
                if (extension == ".txt" || extension == ".md")
                {
                    // Read the uploaded file content
                    var filePath = documentService.GetDocumentPath(documentMeta.Id, owner, documentMeta.FileName);
                    textForAnalysis = await File.ReadAllTextAsync(filePath);
                }
                else
                {
                    // Use filename and existing tags for analysis
                    textForAnalysis = $"Filename: {file.FileName}\nExisting tags: {string.Join(", ", tagList)}";
                }

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
        }

        // Read the document content (for now, we'll need to implement text extraction)
        // This is a simplified version - in production you'd want proper text extraction
        var filePath = documentService.GetDocumentPath(id, owner, document.FileName);
        if (!File.Exists(filePath))
        {
            return Results.NotFound("Document file not found");
        }

        // For demonstration, we'll read text files directly
        // In production, you'd use OCR for images, PDF readers for PDFs, etc.
        string textContent;
        var extension = Path.GetExtension(document.FileName).ToLowerInvariant();
        
        if (extension == ".txt" || extension == ".md")
        {
            textContent = await File.ReadAllTextAsync(filePath);
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
        }

        // Read the document content for AI analysis
        var filePath = documentService.GetDocumentPath(id, owner, document.FileName);
        if (!File.Exists(filePath))
        {
            return Results.NotFound("Document file not found");
        }

        // Extract text content for analysis
        string textContent;
        var extension = Path.GetExtension(document.FileName).ToLowerInvariant();
        
        if (extension == ".txt" || extension == ".md")
        {
            textContent = await File.ReadAllTextAsync(filePath);
        }
        else
        {
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

// Endpoint to suggest tags for new uploads using AI
app.MapPost("/documents/suggest-tags", async (
    [FromBody] SuggestTagsRequest request,
    IDocumentAIService aiService) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(request.TextContent) && string.IsNullOrWhiteSpace(request.FileName))
        {
            return Results.BadRequest("Either text content or filename must be provided");
        }

        var textForAnalysis = !string.IsNullOrWhiteSpace(request.TextContent) 
            ? request.TextContent 
            : $"Filename: {request.FileName}";

        var suggestedTags = await aiService.SuggestTagsAsync(
            textForAnalysis, 
            request.ExistingTags, 
            request.MaxTags);

        var result = new
        {
            SuggestedTags = suggestedTags,
            RequestedMaxTags = request.MaxTags,
            ExistingTags = request.ExistingTags ?? new List<string>(),
            AnalyzedAt = DateTimeOffset.UtcNow
        };

        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error suggesting tags");
        return Results.Problem("An error occurred while suggesting tags");
    }
})
.WithName("SuggestTags")
.WithSummary("Get AI-suggested tags for document content")
.WithDescription("""
    Uses AI to analyze text content or filename and suggest relevant tags for document organization.
    
    **Use Cases:**
    - Tag suggestion before uploading documents
    - Improving existing document organization
    - Standardizing tag naming across documents
    
    **Request Body:**
    ```json
    {
        "textContent": "Document text to analyze (optional)",
        "fileName": "document.pdf (optional if textContent provided)",
        "existingTags": ["tag1", "tag2"] (optional),
        "maxTags": 5
    }
    ```
    """)
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "AI Document Analysis" }];
    return operation;
})
.Accepts<SuggestTagsRequest>("application/json")
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status500InternalServerError);

app.Run();

// Make Program class accessible for testing
public partial class Program { }

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

/// <summary>
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

// Service interface definition is in DocumentModels.cs
