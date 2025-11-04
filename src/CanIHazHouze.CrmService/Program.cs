// Suppress specific nullable return and async warnings until refactor adds explicit non-null contracts
#pragma warning disable CS8603 // Possible null reference return
#pragma warning disable CS1998 // Async method lacks 'await'
using Microsoft.AspNetCore.Mvc;
using Scalar.AspNetCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel;
using System.Net;
using Microsoft.Azure.Cosmos;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations
builder.AddServiceDefaults();
builder.AddMCPSupport();

// Add services to the container
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
builder.AddOpenApiWithAzureContainerAppsServers();

// Add Azure Cosmos DB using Aspire
builder.AddAzureCosmosClient("cosmos");

// Add CRM service
builder.Services.AddScoped<ICrmService, CrmServiceImpl>();

var app = builder.Build();

// Configure the HTTP request pipeline
app.UseExceptionHandler();

// Use CORS
app.UseCors();

app.MapOpenApi();
app.MapScalarApiReference();

// Health check endpoint
app.MapGet("/health", () => Results.Ok("Healthy"))
    .WithName("HealthCheck")
    .WithSummary("Health check endpoint")
    .WithDescription("Simple health check to verify the CRM Service is running and responsive.")
    .WithOpenApi(operation =>
    {
        operation.Tags = [new() { Name = "Service Health" }];
        return operation;
    })
    .Produces<string>(StatusCodes.Status200OK);

// Complaint API endpoints
app.MapPost("/complaints", async (
    [FromBody, Required] CreateComplaintRequest request,
    ICrmService crmService) =>
{
    try
    {
        var complaint = await crmService.CreateComplaintAsync(
            request.CustomerName, 
            request.Title, 
            request.Description);
        return Results.Created($"/complaints/{complaint.Id}", complaint);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ex.Message);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error creating complaint for customer {CustomerName}", request.CustomerName);
        return Results.Problem("An error occurred while creating the complaint");
    }
})
.WithName("CreateComplaint")
.WithSummary("Create a new customer complaint")
.WithDescription("""
    Creates a new complaint in the CRM system.
    
    **Key Features:**
    - Automatic complaint ID generation
    - Initial status set to 'New'
    - Timestamp tracking for creation and updates
    - Per-customer complaint isolation
    
    **Parameters:**
    - `customerName` (required): Name or identifier of the customer
    - `title` (required): Brief title of the complaint
    - `description` (required): Detailed description of the complaint
    
    **Example Request:**
    ```json
    {
        "customerName": "john_doe",
        "title": "Service issue with mortgage approval",
        "description": "The approval process has been delayed for over a month..."
    }
    ```
    """)
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Complaint Management" }];
    return operation;
})
.Produces<Complaint>(StatusCodes.Status201Created)
.Produces<string>(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status500InternalServerError);

app.MapGet("/complaints", async (
    [Required] string customerName,
    ICrmService crmService) =>
{
    try
    {
        var complaints = await crmService.GetComplaintsAsync(customerName);
        return Results.Ok(complaints);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error retrieving complaints for customer {CustomerName}", customerName);
        return Results.Problem("An error occurred while retrieving complaints");
    }
})
.WithName("ListComplaints")
.WithSummary("List all complaints for a customer")
.WithDescription("""
    Retrieves all complaints for a specific customer, including their status and metadata.
    
    **Key Features:**
    - Returns complete complaint information
    - Includes all comments and approvals
    - Results ordered by creation date (most recent first)
    
    **Parameters:**
    - `customerName` (required): Name or identifier of the customer
    
    **Example Response:**
    ```json
    [
        {
            "id": "123e4567-e89b-12d3-a456-426614174000",
            "customerName": "john_doe",
            "title": "Service issue",
            "description": "...",
            "status": "InProgress",
            "createdAt": "2024-06-14T10:30:00Z",
            "updatedAt": "2024-06-15T14:20:00Z",
            "comments": [...],
            "approvals": [...]
        }
    ]
    ```
    """)
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Complaint Management" }];
    var param = operation.Parameters.FirstOrDefault(p => p.Name == "customerName");
    if (param != null)
    {
        param.Description = "Name or identifier of the customer";
        param.Required = true;
        param.Example = new Microsoft.OpenApi.Any.OpenApiString("john_doe");
    }
    return operation;
})
.Produces<IEnumerable<Complaint>>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status500InternalServerError);

app.MapGet("/complaints/recent", async (
    int? limit,
    ICrmService crmService) =>
{
    try
    {
        var complaints = await crmService.GetRecentComplaintsAsync(limit ?? 10);
        return Results.Ok(complaints);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error retrieving recent complaints");
        return Results.Problem("An error occurred while retrieving recent complaints");
    }
})
.WithName("ListRecentComplaints")
.WithSummary("List recent complaints across all customers")
.WithDescription("""
    Retrieves the most recent complaints from all customers, ordered by creation date.
    
    **Key Features:**
    - Returns complaints from all customers
    - Ordered by creation date (most recent first)
    - Configurable limit (default: 10, max: 100)
    - Useful for monitoring and dashboard views
    
    **Parameters:**
    - `limit` (optional): Maximum number of complaints to return (default: 10, max: 100)
    
    **Example Response:**
    ```json
    [
        {
            "id": "123e4567-e89b-12d3-a456-426614174000",
            "customerName": "john_doe",
            "title": "Service issue",
            "description": "...",
            "status": "InProgress",
            "createdAt": "2024-06-14T10:30:00Z",
            "updatedAt": "2024-06-15T14:20:00Z",
            "comments": [...],
            "approvals": [...]
        }
    ]
    ```
    """)
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Complaint Management" }];
    var param = operation.Parameters.FirstOrDefault(p => p.Name == "limit");
    if (param != null)
    {
        param.Description = "Maximum number of complaints to return (default: 10, max: 100)";
        param.Example = new Microsoft.OpenApi.Any.OpenApiInteger(10);
    }
    return operation;
})
.Produces<IEnumerable<Complaint>>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status500InternalServerError);

app.MapGet("/complaints/{id:guid}", async (
    Guid id,
    [Required] string customerName,
    ICrmService crmService) =>
{
    try
    {
        var complaint = await crmService.GetComplaintAsync(id, customerName);
        return complaint is null ? Results.NotFound() : Results.Ok(complaint);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error retrieving complaint {Id} for customer {CustomerName}", id, customerName);
        return Results.Problem("An error occurred while retrieving the complaint");
    }
})
.WithName("GetComplaint")
.WithSummary("Get a specific complaint by ID")
.WithDescription("""
    Retrieves detailed information for a specific complaint.
    
    **Key Features:**
    - Returns complete complaint details including all comments and approvals
    - Access control: only returns complaints for the specified customer
    - Returns 404 if complaint doesn't exist or customer doesn't have access
    
    **Parameters:**
    - `id` (path, required): Unique GUID identifier of the complaint
    - `customerName` (query, required): Name or identifier of the customer
    """)
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Complaint Management" }];
    return operation;
})
.Produces<Complaint>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status500InternalServerError);

app.MapPut("/complaints/{id:guid}/status", async (
    Guid id,
    [Required] string customerName,
    [FromBody, Required] UpdateStatusRequest request,
    ICrmService crmService) =>
{
    try
    {
        var complaint = await crmService.UpdateComplaintStatusAsync(id, customerName, request.Status);
        return complaint is null ? Results.NotFound() : Results.Ok(complaint);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ex.Message);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error updating complaint {Id} status for customer {CustomerName}", id, customerName);
        return Results.Problem("An error occurred while updating the complaint status");
    }
})
.WithName("UpdateComplaintStatus")
.WithSummary("Update complaint status")
.WithDescription("""
    Updates the status of a complaint.
    
    **Available Statuses:**
    - New: Complaint just created
    - InProgress: Being actively worked on
    - Resolved: Issue has been resolved
    - Closed: Complaint is closed (final state)
    
    **Request Body:**
    ```json
    {
        "status": "InProgress"
    }
    ```
    """)
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Complaint Management" }];
    return operation;
})
.Produces<Complaint>(StatusCodes.Status200OK)
.Produces<string>(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status500InternalServerError);

app.MapPost("/complaints/{id:guid}/comments", async (
    Guid id,
    [Required] string customerName,
    [FromBody, Required] AddCommentRequest request,
    ICrmService crmService) =>
{
    try
    {
        var complaint = await crmService.AddCommentAsync(id, customerName, request.AuthorName, request.Text);
        return complaint is null ? Results.NotFound() : Results.Ok(complaint);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ex.Message);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error adding comment to complaint {Id} for customer {CustomerName}", id, customerName);
        return Results.Problem("An error occurred while adding the comment");
    }
})
.WithName("AddComplaintComment")
.WithSummary("Add a comment to a complaint")
.WithDescription("""
    Adds a support comment to an existing complaint.
    
    **Key Features:**
    - Comments are timestamped automatically
    - Support thread-style conversation
    - Author attribution for accountability
    
    **Request Body:**
    ```json
    {
        "authorName": "support_agent_1",
        "text": "We have reviewed your complaint and are working on a resolution."
    }
    ```
    """)
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Complaint Management" }];
    return operation;
})
.Produces<Complaint>(StatusCodes.Status200OK)
.Produces<string>(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status500InternalServerError);

app.MapPost("/complaints/{id:guid}/approvals", async (
    Guid id,
    [Required] string customerName,
    [FromBody, Required] AddApprovalRequest request,
    ICrmService crmService) =>
{
    try
    {
        var complaint = await crmService.AddApprovalAsync(id, customerName, request.ApproverName, request.Decision, request.Comments);
        return complaint is null ? Results.NotFound() : Results.Ok(complaint);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ex.Message);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error adding approval to complaint {Id} for customer {CustomerName}", id, customerName);
        return Results.Problem("An error occurred while adding the approval");
    }
})
.WithName("AddComplaintApproval")
.WithSummary("Add an approval decision to a complaint")
.WithDescription("""
    Records an approval or rejection decision for a complaint.
    
    **Key Features:**
    - Tracks who made the decision and when
    - Supports approval workflow
    - Optional comments for decision rationale
    
    **Available Decisions:**
    - Approved: Complaint resolution is approved
    - Rejected: Complaint resolution is rejected
    - Pending: Decision is pending further review
    
    **Request Body:**
    ```json
    {
        "approverName": "manager_john",
        "decision": "Approved",
        "comments": "Approved for immediate action"
    }
    ```
    """)
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Complaint Management" }];
    return operation;
})
.Produces<Complaint>(StatusCodes.Status200OK)
.Produces<string>(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status500InternalServerError);

app.MapDelete("/complaints/{id:guid}", async (
    Guid id,
    [Required] string customerName,
    ICrmService crmService) =>
{
    try
    {
        var deleted = await crmService.DeleteComplaintAsync(id, customerName);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error deleting complaint {Id} for customer {CustomerName}", id, customerName);
        return Results.Problem("An error occurred while deleting the complaint");
    }
})
.WithName("DeleteComplaint")
.WithSummary("Delete a complaint")
.WithDescription("""
    Permanently deletes a complaint from the system.
    
    **⚠️ Important Warning:**
    This operation is irreversible. The complaint and all associated comments 
    and approvals will be permanently removed.
    
    **Parameters:**
    - `id` (path, required): Unique GUID identifier of the complaint
    - `customerName` (query, required): Name or identifier of the customer
    """)
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Complaint Management" }];
    return operation;
})
.Produces(StatusCodes.Status204NoContent)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status500InternalServerError);

app.MapDelete("/complaints/{id:guid}/comments/{commentId:guid}", async (
    Guid id,
    Guid commentId,
    [Required] string customerName,
    ICrmService crmService) =>
{
    try
    {
        var complaint = await crmService.DeleteCommentAsync(id, commentId, customerName);
        return complaint is null ? Results.NotFound() : Results.Ok(complaint);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error deleting comment {CommentId} from complaint {Id} for customer {CustomerName}", 
            commentId, id, customerName);
        return Results.Problem("An error occurred while deleting the comment");
    }
})
.WithName("DeleteComplaintComment")
.WithSummary("Delete a comment from a complaint")
.WithDescription("""
    Removes a specific comment from a complaint.
    
    **Parameters:**
    - `id` (path, required): Unique GUID identifier of the complaint
    - `commentId` (path, required): Unique GUID identifier of the comment to delete
    - `customerName` (query, required): Name or identifier of the customer
    """)
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Complaint Management" }];
    return operation;
})
.Produces<Complaint>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status500InternalServerError);

app.MapDefaultEndpoints();

// TODO: MCP tool registration needs migration to official SDK
// The official SDK requires using [McpServerToolType] and [McpServerTool] attributes
// or registering tools via builder.Services.AddMcpServer().WithTools<TToolsClass>()
// For now, tools are exposed via REST API endpoints above and can be called directly via HTTP
app.Logger.LogInformation("CrmService REST API endpoints registered (MCP tools pending migration)");

app.Run();

#pragma warning restore CS8603
#pragma warning restore CS1998

// Make Program class accessible for testing
namespace CanIHazHouze.CrmService
{
    public partial class Program { }
}

// Data models with OpenAPI annotations
/// <summary>
/// Request model for creating a new complaint
/// </summary>
public record CreateComplaintRequest(
    [property: Description("Name or identifier of the customer"), Required] string CustomerName,
    [property: Description("Brief title of the complaint"), Required, StringLength(200)] string Title,
    [property: Description("Detailed description of the complaint"), Required, StringLength(2000)] string Description
);

/// <summary>
/// Request model for updating complaint status
/// </summary>
public record UpdateStatusRequest(
    [property: Description("New status for the complaint"), Required] ComplaintStatus Status
);

/// <summary>
/// Request model for adding a comment
/// </summary>
public record AddCommentRequest(
    [property: Description("Name of the comment author"), Required] string AuthorName,
    [property: Description("Comment text"), Required, StringLength(1000)] string Text
);

/// <summary>
/// Request model for adding an approval
/// </summary>
public record AddApprovalRequest(
    [property: Description("Name of the approver"), Required] string ApproverName,
    [property: Description("Approval decision"), Required] ApprovalDecision Decision,
    [property: Description("Optional comments about the decision"), StringLength(500)] string? Comments
);

/// <summary>
/// Complaint status enumeration
/// </summary>
public enum ComplaintStatus
{
    New,
    InProgress,
    Solved,
    Rejected
}

/// <summary>
/// Approval decision enumeration
/// </summary>
public enum ApprovalDecision
{
    Pending,
    Approved,
    Rejected
}

/// <summary>
/// Comment on a complaint
/// </summary>
public class ComplaintComment
{
    [Description("Unique identifier for the comment")]
    public Guid Id { get; set; }
    
    [Description("Name of the comment author")]
    public string AuthorName { get; set; } = string.Empty;
    
    [Description("Comment text")]
    public string Text { get; set; } = string.Empty;
    
    [Description("Timestamp when the comment was created")]
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Approval decision for a complaint
/// </summary>
public class ComplaintApproval
{
    [Description("Unique identifier for the approval")]
    public Guid Id { get; set; }
    
    [Description("Name of the approver")]
    public string ApproverName { get; set; } = string.Empty;
    
    [Description("Approval decision")]
    public ApprovalDecision Decision { get; set; }
    
    [Description("Optional comments about the decision")]
    public string? Comments { get; set; }
    
    [Description("Timestamp when the approval was created")]
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Customer complaint
/// </summary>
public class Complaint
{
    [Description("Cosmos DB document id")]
    [JsonIgnore] // Don't serialize this in API responses
    public string id { get; set; } = string.Empty;
    
    [Description("Customer name (partition key)")]
    [JsonIgnore] // Don't serialize this in API responses
    public string customerName { get; set; } = string.Empty;
    
    [Description("Unique identifier for the complaint")]
    public Guid Id { get; set; }
    
    [Description("Name or identifier of the customer")]
    public string CustomerName { get; set; } = string.Empty;
    
    [Description("Brief title of the complaint")]
    public string Title { get; set; } = string.Empty;
    
    [Description("Detailed description of the complaint")]
    public string Description { get; set; } = string.Empty;
    
    [Description("Current status of the complaint")]
    public ComplaintStatus Status { get; set; }
    
    [Description("Timestamp when the complaint was created")]
    public DateTime CreatedAt { get; set; }
    
    [Description("Timestamp when the complaint was last updated")]
    public DateTime UpdatedAt { get; set; }
    
    [Description("List of comments on the complaint")]
    public List<ComplaintComment> Comments { get; set; } = new();
    
    [Description("List of approval decisions for the complaint")]
    public List<ComplaintApproval> Approvals { get; set; } = new();
    
    [Description("Document type discriminator")]
    [JsonIgnore] // Don't serialize this in API responses
    public string Type { get; set; } = "complaint";
}

// Service interface and implementation
/// <summary>
/// Interface for CRM service operations
/// </summary>
public interface ICrmService
{
    Task<Complaint> CreateComplaintAsync(string customerName, string title, string description);
    Task<Complaint?> GetComplaintAsync(Guid id, string customerName);
    Task<IEnumerable<Complaint>> GetComplaintsAsync(string customerName);
    Task<IEnumerable<Complaint>> GetRecentComplaintsAsync(int limit);
    Task<Complaint?> UpdateComplaintStatusAsync(Guid id, string customerName, ComplaintStatus status);
    Task<Complaint?> AddCommentAsync(Guid id, string customerName, string authorName, string text);
    Task<Complaint?> AddApprovalAsync(Guid id, string customerName, string approverName, ApprovalDecision decision, string? comments);
    Task<bool> DeleteComplaintAsync(Guid id, string customerName);
    Task<Complaint?> DeleteCommentAsync(Guid id, Guid commentId, string customerName);
}

/// <summary>
/// Implementation of CRM service using Cosmos DB
/// </summary>
public class CrmServiceImpl : ICrmService
{
    private readonly CosmosClient _cosmosClient;
    private readonly ILogger<CrmServiceImpl> _logger;
    private readonly Microsoft.Azure.Cosmos.Container _container;

    public CrmServiceImpl(CosmosClient cosmosClient, ILogger<CrmServiceImpl> logger)
    {
        _cosmosClient = cosmosClient;
        _logger = logger;
        _container = _cosmosClient.GetContainer("houze", "crm");
    }

    public async Task<Complaint> CreateComplaintAsync(string customerName, string title, string description)
    {
        if (string.IsNullOrWhiteSpace(customerName))
            throw new ArgumentException("Customer name cannot be empty", nameof(customerName));
        
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title cannot be empty", nameof(title));
        
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description cannot be empty", nameof(description));

        var complaint = new Complaint
        {
            id = $"complaint:{Guid.NewGuid()}",
            customerName = customerName,
            Id = Guid.NewGuid(),
            CustomerName = customerName,
            Title = title,
            Description = description,
            Status = ComplaintStatus.New,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Type = "complaint"
        };

        await _container.CreateItemAsync(complaint, new PartitionKey(customerName));
        
        _logger.LogInformation("Created complaint {ComplaintId} for customer {CustomerName}", complaint.Id, customerName);
        return complaint;
    }

    public async Task<Complaint?> GetComplaintAsync(Guid id, string customerName)
    {
        if (string.IsNullOrWhiteSpace(customerName))
            throw new ArgumentException("Customer name cannot be empty", nameof(customerName));

        try
        {
            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.Id = @id AND c.customerName = @customerName AND c.Type = @type")
                .WithParameter("@id", id)
                .WithParameter("@customerName", customerName)
                .WithParameter("@type", "complaint");

            var iterator = _container.GetItemQueryIterator<Complaint>(query);
            if (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                return response.FirstOrDefault();
            }
            return null;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IEnumerable<Complaint>> GetComplaintsAsync(string customerName)
    {
        if (string.IsNullOrWhiteSpace(customerName))
            throw new ArgumentException("Customer name cannot be empty", nameof(customerName));

        try
        {
            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.customerName = @customerName AND c.Type = @type ORDER BY c.CreatedAt DESC")
                .WithParameter("@customerName", customerName)
                .WithParameter("@type", "complaint");

            var iterator = _container.GetItemQueryIterator<Complaint>(query);
            var complaints = new List<Complaint>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                complaints.AddRange(response);
            }

            return complaints;
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Error retrieving complaints for customer {CustomerName}", customerName);
            throw;
        }
    }

    public async Task<IEnumerable<Complaint>> GetRecentComplaintsAsync(int limit)
    {
        // Enforce reasonable limits
        if (limit < 1) limit = 10;
        if (limit > 100) limit = 100;

        try
        {
            var query = new QueryDefinition(
                "SELECT TOP @limit * FROM c WHERE c.Type = @type ORDER BY c.CreatedAt DESC")
                .WithParameter("@limit", limit)
                .WithParameter("@type", "complaint");

            var iterator = _container.GetItemQueryIterator<Complaint>(query);
            var complaints = new List<Complaint>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                complaints.AddRange(response);
            }

            _logger.LogInformation("Retrieved {Count} recent complaints", complaints.Count);
            return complaints;
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Error retrieving recent complaints");
            throw;
        }
    }

    public async Task<Complaint?> UpdateComplaintStatusAsync(Guid id, string customerName, ComplaintStatus status)
    {
        var complaint = await GetComplaintAsync(id, customerName);
        if (complaint == null) return null;

        complaint.Status = status;
        complaint.UpdatedAt = DateTime.UtcNow;

        await _container.ReplaceItemAsync(complaint, complaint.id, new PartitionKey(customerName));
        
        _logger.LogInformation("Updated complaint {ComplaintId} status to {Status} for customer {CustomerName}", 
            id, status, customerName);
        
        return complaint;
    }

    public async Task<Complaint?> AddCommentAsync(Guid id, string customerName, string authorName, string text)
    {
        if (string.IsNullOrWhiteSpace(authorName))
            throw new ArgumentException("Author name cannot be empty", nameof(authorName));
        
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Comment text cannot be empty", nameof(text));

        var complaint = await GetComplaintAsync(id, customerName);
        if (complaint == null) return null;

        var comment = new ComplaintComment
        {
            Id = Guid.NewGuid(),
            AuthorName = authorName,
            Text = text,
            CreatedAt = DateTime.UtcNow
        };

        complaint.Comments.Add(comment);
        complaint.UpdatedAt = DateTime.UtcNow;

        await _container.ReplaceItemAsync(complaint, complaint.id, new PartitionKey(customerName));
        
        _logger.LogInformation("Added comment to complaint {ComplaintId} by {AuthorName} for customer {CustomerName}", 
            id, authorName, customerName);
        
        return complaint;
    }

    public async Task<Complaint?> AddApprovalAsync(Guid id, string customerName, string approverName, ApprovalDecision decision, string? comments)
    {
        if (string.IsNullOrWhiteSpace(approverName))
            throw new ArgumentException("Approver name cannot be empty", nameof(approverName));

        var complaint = await GetComplaintAsync(id, customerName);
        if (complaint == null) return null;

        var approval = new ComplaintApproval
        {
            Id = Guid.NewGuid(),
            ApproverName = approverName,
            Decision = decision,
            Comments = comments,
            CreatedAt = DateTime.UtcNow
        };

        complaint.Approvals.Add(approval);
        complaint.UpdatedAt = DateTime.UtcNow;

        await _container.ReplaceItemAsync(complaint, complaint.id, new PartitionKey(customerName));
        
        _logger.LogInformation("Added {Decision} approval to complaint {ComplaintId} by {ApproverName} for customer {CustomerName}", 
            decision, id, approverName, customerName);
        
        return complaint;
    }

    public async Task<bool> DeleteComplaintAsync(Guid id, string customerName)
    {
        try
        {
            var complaint = await GetComplaintAsync(id, customerName);
            if (complaint == null) return false;

            await _container.DeleteItemAsync<Complaint>(complaint.id, new PartitionKey(customerName));
            
            _logger.LogInformation("Deleted complaint {ComplaintId} for customer {CustomerName}", id, customerName);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Error deleting complaint {ComplaintId} for customer {CustomerName}", id, customerName);
            throw;
        }
    }

    public async Task<Complaint?> DeleteCommentAsync(Guid id, Guid commentId, string customerName)
    {
        var complaint = await GetComplaintAsync(id, customerName);
        if (complaint == null) return null;

        var comment = complaint.Comments.FirstOrDefault(c => c.Id == commentId);
        if (comment == null) return null;

        complaint.Comments.Remove(comment);
        complaint.UpdatedAt = DateTime.UtcNow;

        await _container.ReplaceItemAsync(complaint, complaint.id, new PartitionKey(customerName));
        
        _logger.LogInformation("Deleted comment {CommentId} from complaint {ComplaintId} for customer {CustomerName}", 
            commentId, id, customerName);
        
        return complaint;
    }
}

// MCP Tool Request Models
/// <summary>
/// Request model for creating a complaint via MCP
/// </summary>
/// <param name="CustomerName">Name or identifier of the customer</param>
/// <param name="Title">Brief title of the complaint</param>
/// <param name="Description">Detailed description of the complaint</param>
public record CreateComplaintMcpRequest(string CustomerName, string Title, string Description);

/// <summary>
/// Request model for getting complaints via MCP
/// </summary>
/// <param name="CustomerName">Name or identifier of the customer</param>
public record GetComplaintsMcpRequest(string CustomerName);

/// <summary>
/// Request model for getting recent complaints via MCP
/// </summary>
/// <param name="Limit">Maximum number of complaints to return (default: 10, max: 100)</param>
public record GetRecentComplaintsMcpRequest(int? Limit = 10);

/// <summary>
/// Request model for getting a specific complaint via MCP
/// </summary>
/// <param name="Id">Unique GUID identifier of the complaint</param>
/// <param name="CustomerName">Name or identifier of the customer</param>
public record GetComplaintMcpRequest(Guid Id, string CustomerName);

/// <summary>
/// Request model for updating complaint status via MCP
/// </summary>
/// <param name="Id">Unique GUID identifier of the complaint</param>
/// <param name="CustomerName">Name or identifier of the customer</param>
/// <param name="Status">New status for the complaint</param>
public record UpdateComplaintStatusMcpRequest(Guid Id, string CustomerName, ComplaintStatus Status);

/// <summary>
/// Request model for adding a comment via MCP
/// </summary>
/// <param name="Id">Unique GUID identifier of the complaint</param>
/// <param name="CustomerName">Name or identifier of the customer</param>
/// <param name="AuthorName">Name of the comment author</param>
/// <param name="Text">Comment text</param>
public record AddCommentMcpRequest(Guid Id, string CustomerName, string AuthorName, string Text);

/// <summary>
/// Request model for adding an approval via MCP
/// </summary>
/// <param name="Id">Unique GUID identifier of the complaint</param>
/// <param name="CustomerName">Name or identifier of the customer</param>
/// <param name="ApproverName">Name of the approver</param>
/// <param name="Decision">Approval decision</param>
/// <param name="Comments">Optional comments about the decision</param>
public record AddApprovalMcpRequest(Guid Id, string CustomerName, string ApproverName, ApprovalDecision Decision, string? Comments);

/// <summary>
/// Request model for deleting a complaint via MCP
/// </summary>
/// <param name="Id">Unique GUID identifier of the complaint</param>
/// <param name="CustomerName">Name or identifier of the customer</param>
public record DeleteComplaintMcpRequest(Guid Id, string CustomerName);
