using CanIHazHouze.AgentService.BackgroundServices;
using CanIHazHouze.AgentService.Models;
using CanIHazHouze.AgentService.Security;
using CanIHazHouze.AgentService.Services;
using Microsoft.AspNetCore.Mvc;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add MCP server support with AgentTools
builder.AddMCPSupport()
    .WithTools<CanIHazHouze.AgentService.McpTools.AgentTools>();

// Add services to the container.
builder.Services.AddProblemDetails();

// Add CORS for SignalR
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Configure SignalR to use Newtonsoft.Json (matches our model attributes)
builder.Services.AddSignalR()
    .AddNewtonsoftJsonProtocol();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.AddOpenApiWithAzureContainerAppsServers();

// Configure agent storage options
builder.Services.Configure<AgentStorageOptions>(
    builder.Configuration.GetSection("AgentStorage"));

// Add Azure Cosmos DB using Aspire
builder.AddAzureCosmosClient("cosmos");

// Keyless Azure OpenAI client (DefaultAzureCredential)
// REQUIRED: AppHost must be running with ConnectionStrings:openai configured
// The connection string is injected by Aspire via WithReference(openai)
var openAiConn = builder.Configuration.GetConnectionString("openai");
if (string.IsNullOrWhiteSpace(openAiConn))
{
    // Enhanced diagnostics to help debug configuration issues
    var allConnStrings = builder.Configuration.GetSection("ConnectionStrings").GetChildren()
        .Select(c => $"{c.Key}={(string.IsNullOrWhiteSpace(c.Value) ? "<empty>" : "***")}")
        .ToList();
    
    var configSources = string.Join(", ", builder.Configuration.AsEnumerable()
        .Where(kvp => kvp.Key.Contains("openai", StringComparison.OrdinalIgnoreCase))
        .Select(kvp => $"{kvp.Key}={(string.IsNullOrWhiteSpace(kvp.Value) ? "<empty>" : "***")}"));
    
    throw new InvalidOperationException(
        $"OpenAI connection string is required but was not found or is empty.\n" +
        $"Available connection strings: {(allConnStrings.Any() ? string.Join(", ", allConnStrings) : "none")}\n" +
        $"OpenAI-related config keys: {(string.IsNullOrEmpty(configSources) ? "none" : configSources)}\n\n" +
        $"When running via AppHost: Make sure AppHost has the connection string configured:\n" +
        $"  cd src/CanIHazHouze.AppHost && dotnet user-secrets set \"ConnectionStrings:openai\" \"Endpoint=https://...\"\n\n" +
        $"When running AgentService directly: Set the secret in this project:\n" +
        $"  cd src/CanIHazHouze.AgentService && dotnet user-secrets set \"ConnectionStrings:openai\" \"Endpoint=https://...\"");
}

string? openAiEndpoint = null;
foreach (var part in openAiConn.Split(';', StringSplitOptions.RemoveEmptyEntries))
{
    if (part.StartsWith("Endpoint=", StringComparison.OrdinalIgnoreCase))
    {
        openAiEndpoint = part.Substring("Endpoint=".Length).Trim();
        break;
    }
}

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

// Configure OpenAIConfiguration for AgentExecutionService
builder.Services.Configure<CanIHazHouze.AgentService.Configuration.OpenAIConfiguration>(options =>
{
    options.Endpoint = openAiEndpoint;
    options.ApiKey = string.Empty; // Using keyless authentication with DefaultAzureCredential
});

// Add HttpClient for MCP client service
builder.Services.AddHttpClient();

// Add MCP client service for connecting to MCP servers
builder.Services.AddSingleton<IMcpClientService, McpClientService>();

// Add agent services
builder.Services.AddScoped<IAgentStorageService, AgentStorageService>();
builder.Services.AddScoped<IAgentExecutionService, AgentExecutionService>();
builder.Services.AddScoped<MultiTurnAgentExecutor>();

// Add SignalR event broadcaster
builder.Services.AddSingleton<IAgentEventBroadcaster, AgentEventBroadcaster>();

// Remove incorrect Cosmos client registration for openai; we now added explicit AzureOpenAIClient above.

// Add background service for long-running agent tasks
builder.Services.AddSingleton<AgentExecutionBackgroundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AgentExecutionBackgroundService>());

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

// Use CORS
app.UseCors();

app.MapOpenApi();
app.MapScalarApiReference();

// Health check endpoint
app.MapGet("/health", () => Results.Ok("Healthy"))
    .WithName("HealthCheck")
    .WithSummary("Health check endpoint")
    .WithDescription("Simple health check to verify the Agent Service is running and responsive.")
    .WithOpenApi(operation =>
    {
        operation.Tags = [new() { Name = "Service Health" }];
        return operation;
    })
    .Produces<string>(StatusCodes.Status200OK);

// Get available models endpoint
app.MapGet("/models", () => Results.Ok(AvailableModels.All))
    .WithName("GetAvailableModels")
    .WithSummary("Get available AI models")
    .WithDescription("Retrieves the list of available AI model deployments that can be used for agent execution.")
    .WithOpenApi(operation =>
    {
        operation.Tags = [new() { Name = "Configuration" }];
        return operation;
    })
    .Produces<List<ModelDeployment>>(StatusCodes.Status200OK);

// Agent CRUD endpoints
app.MapGet("/agents", async (IAgentStorageService storage) =>
{
    try
    {
        var agents = await storage.GetAllAgentsAsync();
        return Results.Ok(agents);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error retrieving agents");
        return Results.Problem("An error occurred while retrieving agents");
    }
})
.WithName("GetAgents")
.WithSummary("Get all agents")
.WithDescription("Retrieves all AI agents.")
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Agents" }];
    return operation;
})
.Produces<List<Agent>>(StatusCodes.Status200OK);

// Diagnostics: raw agent documents (including runs) for troubleshooting listing issues
app.MapGet("/agents/raw", async (IAgentStorageService storage, Microsoft.Azure.Cosmos.CosmosClient cosmosClient) =>
{
    try
    {
        var db = cosmosClient.GetDatabase("houze");
        var container = db.GetContainer("agents");
        var query = new Microsoft.Azure.Cosmos.QueryDefinition("SELECT * FROM c");
        var iterator = container.GetItemQueryIterator<dynamic>(query);
        var docs = new List<object>();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            docs.AddRange(response);
        }
        return Results.Ok(docs);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error retrieving raw agent documents");
        return Results.Problem("Diagnostics failed retrieving raw documents");
    }
})
.WithName("GetRawAgentDocuments")
.WithSummary("Diagnostics: Get all raw documents in agents container")
.WithDescription("Returns every document from the agents container regardless of entityType to debug missing entries.")
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Diagnostics" }];
    return operation;
})
.Produces<List<object>>(StatusCodes.Status200OK);

// Diagnostics: OpenAI configuration
app.MapGet("/diagnostics/openai", (IServiceProvider sp) =>
{
    var client = sp.GetService<Azure.AI.OpenAI.AzureOpenAIClient>();
    return Results.Ok(new
    {
        configured = client != null,
        endpoint = openAiEndpoint,
        timestampUtc = DateTime.UtcNow
    });
})
.WithName("GetOpenAIDiagnostics")
.WithSummary("Diagnostics: OpenAI configuration mode")
.WithDescription("Shows whether the service is using the real Azure OpenAI client or dummy execution services.")
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Diagnostics" }];
    return operation;
})
.Produces<object>(StatusCodes.Status200OK);

// Diagnostics: counts by entityType
app.MapGet("/agents/counts", async (IAgentStorageService storage, Microsoft.Azure.Cosmos.CosmosClient cosmosClient) =>
{
    try
    {
        var db = cosmosClient.GetDatabase("houze");
        var container = db.GetContainer("agents");
        var query = new Microsoft.Azure.Cosmos.QueryDefinition("SELECT c.entityType AS entityType, COUNT(1) AS count FROM c GROUP BY c.entityType");
        var iterator = container.GetItemQueryIterator<dynamic>(query);
        var results = new List<object>();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }
        return Results.Ok(results);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error retrieving entity type counts");
        return Results.Problem("Diagnostics failed retrieving counts");
    }
})
.WithName("GetAgentEntityCounts")
.WithSummary("Diagnostics: Get counts by entityType")
.WithDescription("Returns counts of documents grouped by entityType to verify agent documents exist.")
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Diagnostics" }];
    return operation;
})
.Produces<List<object>>(StatusCodes.Status200OK);

app.MapGet("/agents/{id}", async (string id, IAgentStorageService storage) =>
{
    try
    {
        var agent = await storage.GetAgentAsync(id);
        if (agent == null)
        {
            return Results.NotFound($"Agent {id} not found");
        }
        return Results.Ok(agent);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error retrieving agent {Id}", LogSanitizer.Sanitize(id));
        return Results.Problem("An error occurred while retrieving the agent");
    }
})
.WithName("GetAgent")
.WithSummary("Get a specific agent")
.WithDescription("Retrieves a specific AI agent by ID.")
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Agents" }];
    return operation;
})
.Produces<Agent>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);

app.MapPost("/agents", async ([FromBody] Agent agent, IAgentStorageService storage) =>
{
    try
    {
        var created = await storage.CreateAgentAsync(agent);
        return Results.Created($"/agents/{created.Id}", created);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error creating agent");
        return Results.Problem("An error occurred while creating the agent");
    }
})
.WithName("CreateAgent")
.WithSummary("Create a new agent")
.WithDescription("Creates a new AI agent with the specified configuration.")
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Agents" }];
    return operation;
})
.Produces<Agent>(StatusCodes.Status201Created);

app.MapPut("/agents/{id}", async (string id, [FromBody] Agent agent, IAgentStorageService storage) =>
{
    try
    {
        agent.Id = id;
        var updated = await storage.UpdateAgentAsync(agent);
        return Results.Ok(updated);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error updating agent {Id}", LogSanitizer.Sanitize(id));
        return Results.Problem("An error occurred while updating the agent");
    }
})
.WithName("UpdateAgent")
.WithSummary("Update an agent")
.WithDescription("Updates an existing AI agent's configuration.")
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Agents" }];
    return operation;
})
.Produces<Agent>(StatusCodes.Status200OK);

app.MapDelete("/agents/{id}", async (string id, IAgentStorageService storage) =>
{
    try
    {
        await storage.DeleteAgentAsync(id);
        return Results.NoContent();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error deleting agent {Id}", LogSanitizer.Sanitize(id));
        return Results.Problem("An error occurred while deleting the agent");
    }
})
.WithName("DeleteAgent")
.WithSummary("Delete an agent")
.WithDescription("Deletes an AI agent.")
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Agents" }];
    return operation;
})
.Produces(StatusCodes.Status204NoContent);

// Agent execution endpoints
app.MapPost("/agents/{id}/run", async (
    string id,
    [FromBody] Dictionary<string, string> inputValues,
    IAgentExecutionService executionService) =>
{
    try
    {
        var run = await executionService.ExecuteAgentAsync(id, inputValues);
        return Results.Ok(run);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error executing agent {Id}", LogSanitizer.Sanitize(id));
        return Results.Problem($"An error occurred while executing the agent: {ex.Message}");
    }
})
.WithName("RunAgent")
.WithSummary("Execute an agent")
.WithDescription("Executes an AI agent with the provided input values.")
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Agent Execution" }];
    return operation;
})
.Produces<AgentRun>(StatusCodes.Status200OK);

app.MapGet("/runs/{agentId}/{id}", async (string agentId, string id, IAgentStorageService storage) =>
{
    try
    {
        var run = await storage.GetRunAsync(id, agentId);
        if (run == null)
        {
            return Results.NotFound($"Run {id} not found");
        }
        return Results.Ok(run);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error retrieving run {Id} for agent {AgentId}", LogSanitizer.Sanitize(id), LogSanitizer.Sanitize(agentId));
        return Results.Problem("An error occurred while retrieving the run");
    }
})
.WithName("GetRun")
.WithSummary("Get a specific run")
.WithDescription("Retrieves a specific agent run by ID.")
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Agent Execution" }];
    return operation;
})
.Produces<AgentRun>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);

app.MapGet("/agents/{agentId}/runs", async (string agentId, IAgentStorageService storage) =>
{
    try
    {
        var runs = await storage.GetRunsByAgentAsync(agentId);
        return Results.Ok(runs);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error retrieving runs for agent {AgentId}", LogSanitizer.Sanitize(agentId));
        return Results.Problem("An error occurred while retrieving runs");
    }
})
.WithName("GetAgentRuns")
.WithSummary("Get all runs for an agent")
.WithDescription("Retrieves all execution runs for a specific agent.")
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Agent Execution" }];
    return operation;
})
.Produces<List<AgentRun>>(StatusCodes.Status200OK);

app.MapDelete("/runs/{agentId}/{id}", async (string agentId, string id, IAgentStorageService storage) =>
{
    try
    {
        await storage.DeleteRunAsync(id, agentId);
        return Results.NoContent();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error deleting run {RunId} for agent {AgentId}", LogSanitizer.Sanitize(id), LogSanitizer.Sanitize(agentId));
        return Results.Problem("An error occurred while deleting the run");
    }
})
.WithName("DeleteRun")
.WithSummary("Delete a run")
.WithDescription("Deletes a specific agent execution run.")
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Agent Execution" }];
    return operation;
})
.Produces(StatusCodes.Status204NoContent);

app.MapDelete("/agents/{agentId}/runs", async (string agentId, IAgentStorageService storage) =>
{
    try
    {
        var count = await storage.DeleteAllRunsAsync(agentId);
        return Results.Ok(new { message = $"Deleted {count} runs", count });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error deleting all runs for agent {AgentId}", LogSanitizer.Sanitize(agentId));
        return Results.Problem("An error occurred while deleting runs");
    }
})
.WithName("DeleteAllAgentRuns")
.WithSummary("Delete all runs for an agent")
.WithDescription("Deletes all execution runs for a specific agent.")
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Agent Execution" }];
    return operation;
})
.Produces(StatusCodes.Status200OK);

// Background execution endpoints
app.MapPost("/agents/{id}/run-async", async (
    string id,
    [FromBody] Dictionary<string, string> inputValues,
    AgentExecutionBackgroundService backgroundService,
    IAgentStorageService storage) =>
{
    try
    {
        var agent = await storage.GetAgentAsync(id);
        if (agent == null)
        {
            return Results.NotFound($"Agent {id} not found");
        }

        var runId = await backgroundService.QueueAgentExecutionAsync(id, inputValues);
        return Results.Accepted($"/runs/{id}/{runId}", new { runId, agentId = id, status = "queued" });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error queuing agent {Id} for execution", LogSanitizer.Sanitize(id));
        return Results.Problem($"An error occurred while queuing the agent: {ex.Message}");
    }
})
.WithName("RunAgentAsync")
.WithSummary("Execute an agent asynchronously")
.WithDescription("Queues an AI agent for background execution and returns immediately with a run ID.")
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Agent Execution" }];
    return operation;
})
.Produces(StatusCodes.Status202Accepted);

app.MapPost("/runs/{agentId}/{id}/pause", async (
    string agentId,
    string id,
    AgentExecutionBackgroundService backgroundService,
    IAgentStorageService storage) =>
{
    try
    {
        var run = await storage.GetRunAsync(id, agentId);
        if (run == null)
        {
            return Results.NotFound($"Run {id} not found");
        }

        if (backgroundService.PauseAgent(id))
        {
            run.Status = "paused";
            run.PausedAt = DateTime.UtcNow;
            await storage.UpdateRunAsync(run);
            return Results.Ok(new { message = "Run paused successfully", runId = id });
        }

        return Results.BadRequest("Run is not currently running");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error pausing run {Id}", LogSanitizer.Sanitize(id));
        return Results.Problem($"An error occurred while pausing the run: {ex.Message}");
    }
})
.WithName("PauseRun")
.WithSummary("Pause a running agent")
.WithDescription("Pauses an actively running agent execution.")
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Agent Execution" }];
    return operation;
})
.Produces(StatusCodes.Status200OK);

app.MapPost("/runs/{agentId}/{id}/resume", async (
    string agentId,
    string id,
    AgentExecutionBackgroundService backgroundService,
    IAgentStorageService storage) =>
{
    try
    {
        var run = await storage.GetRunAsync(id, agentId);
        if (run == null)
        {
            return Results.NotFound($"Run {id} not found");
        }

        if (run.Status != "paused")
        {
            return Results.BadRequest("Run is not paused");
        }

        run.Status = "running";
        run.PausedAt = null;
        await storage.UpdateRunAsync(run);

        // Note: This updates the status, but the background service needs to check and continue
        return Results.Ok(new { message = "Run status updated to running", runId = id });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error resuming run {Id}", LogSanitizer.Sanitize(id));
        return Results.Problem($"An error occurred while resuming the run: {ex.Message}");
    }
})
.WithName("ResumeRun")
.WithSummary("Resume a paused agent")
.WithDescription("Resumes a paused agent execution.")
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Agent Execution" }];
    return operation;
})
.Produces(StatusCodes.Status200OK);

app.MapPost("/runs/{agentId}/{id}/cancel", (
    string agentId,
    string id,
    AgentExecutionBackgroundService backgroundService) =>
{
    try
    {
        if (backgroundService.CancelAgent(id))
        {
            return Results.Ok(new { message = "Run cancellation requested", runId = id });
        }

        return Results.NotFound("Run not found or not currently running");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error cancelling run {Id}", LogSanitizer.Sanitize(id));
        return Results.Problem($"An error occurred while cancelling the run: {ex.Message}");
    }
})
.WithName("CancelRun")
.WithSummary("Cancel a running agent")
.WithDescription("Cancels an actively running agent execution.")
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Agent Execution" }];
    return operation;
})
.Produces(StatusCodes.Status200OK);

app.MapGet("/runs/active", (AgentExecutionBackgroundService backgroundService) =>
{
    try
    {
        var runningAgents = backgroundService.GetRunningAgentIds();
        return Results.Ok(new { activeRuns = runningAgents, count = runningAgents.Count });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error retrieving active runs");
        return Results.Problem("An error occurred while retrieving active runs");
    }
})
.WithName("GetActiveRuns")
.WithSummary("Get all active agent runs")
.WithDescription("Retrieves a list of all currently running agent execution IDs.")
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Agent Execution" }];
    return operation;
})
.Produces(StatusCodes.Status200OK);

// Chat continuation endpoint
app.MapPost("/runs/{agentId}/{id}/chat", async (
    string agentId,
    string id,
    [FromBody] ChatMessageRequest request,
    IAgentExecutionService executionService) =>
{
    try
    {
        var run = await executionService.ContinueChatAsync(agentId, id, request.Message);
        return Results.Ok(run);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(ex.Message);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error continuing chat for run {RunId}", LogSanitizer.Sanitize(id));
        return Results.Problem($"An error occurred while continuing the chat: {ex.Message}");
    }
})
.WithName("ContinueChat")
.WithSummary("Continue chatting with a completed agent")
.WithDescription("Sends a message to continue the conversation with a completed agent run, maintaining context and tool access.")
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Agent Execution" }];
    return operation;
})
.Produces<AgentRun>(StatusCodes.Status200OK);

// Map SignalR hub
app.MapHub<CanIHazHouze.AgentService.Hubs.AgentHub>("/hubs/agent");

// Map MCP endpoints
app.MapDefaultEndpoints();

app.Logger.LogInformation("🔧 AgentService MCP tools registered at /mcp endpoint");

app.Run();

// DTOs
public record ChatMessageRequest(string Message);

// Make Program class accessible for testing (WebApplicationFactory)
namespace CanIHazHouze.AgentService
{
    public partial class Program { }
}
