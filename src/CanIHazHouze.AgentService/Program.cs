using CanIHazHouze.AgentService.BackgroundServices;
using CanIHazHouze.AgentService.Configuration;
using CanIHazHouze.AgentService.Extensions;
using CanIHazHouze.AgentService.Models;
using CanIHazHouze.AgentService.Security;
using CanIHazHouze.AgentService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.SemanticKernel;
using Scalar.AspNetCore;

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
builder.AddOpenApiWithAzureContainerAppsServers();

// Configure agent storage options
builder.Services.Configure<AgentStorageOptions>(
    builder.Configuration.GetSection("AgentStorage"));

// Add Azure Cosmos DB using Aspire
builder.AddAzureCosmosClient("cosmos");

// Add Azure OpenAI configuration using Aspire patterns
// This properly integrates with Aspire's connection string management from AppHost
// and registers both OpenAIConfiguration (for Semantic Kernel) and AzureOpenAIClient (for direct SDK access)
builder.AddAzureOpenAIConfiguration("openai");

// Add agent services
builder.Services.AddScoped<IAgentStorageService, AgentStorageService>();
builder.Services.AddScoped<IAgentExecutionService, AgentExecutionService>();
builder.Services.AddScoped<MultiTurnAgentExecutor>();

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

app.Run();
