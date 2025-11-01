using CanIHazHouze.AgentService.Models;
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

// Add OpenAI configuration
var openAiConnectionString = builder.Configuration.GetConnectionString("openai");
if (!string.IsNullOrEmpty(openAiConnectionString))
{
    // Parse connection string format: Endpoint=https://...;Key=...
    var parts = openAiConnectionString.Split(';');
    var endpoint = parts.FirstOrDefault(p => p.StartsWith("Endpoint="))?.Split('=')[1];
    var key = parts.FirstOrDefault(p => p.StartsWith("Key="))?.Split('=')[1];
    
    if (!string.IsNullOrEmpty(endpoint) && !string.IsNullOrEmpty(key))
    {
        // Get deployment name from configuration, default to gpt-4o-mini
        var deploymentName = builder.Configuration["OpenAI:DeploymentName"] ?? "gpt-4o-mini";
        
        var kernelBuilder = builder.Services.AddKernel();
        kernelBuilder.AddAzureOpenAIChatCompletion(
            deploymentName: deploymentName,
            endpoint: endpoint,
            apiKey: key);
    }
}

// Add agent services
builder.Services.AddScoped<IAgentStorageService, AgentStorageService>();
builder.Services.AddScoped<IAgentExecutionService, AgentExecutionService>();

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

// Agent CRUD endpoints
app.MapGet("/agents/{owner}", async (string owner, IAgentStorageService storage) =>
{
    try
    {
        var agents = await storage.GetAgentsByOwnerAsync(owner);
        return Results.Ok(agents);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error retrieving agents for owner {Owner}", owner);
        return Results.Problem("An error occurred while retrieving agents");
    }
})
.WithName("GetAgents")
.WithSummary("Get all agents for a user")
.WithDescription("Retrieves all AI agents owned by the specified user.")
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Agents" }];
    return operation;
})
.Produces<List<Agent>>(StatusCodes.Status200OK);

app.MapGet("/agents/{owner}/{id}", async (string owner, string id, IAgentStorageService storage) =>
{
    try
    {
        var agent = await storage.GetAgentAsync(id, owner);
        if (agent == null)
        {
            return Results.NotFound($"Agent {id} not found");
        }
        return Results.Ok(agent);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error retrieving agent {Id} for owner {Owner}", id, owner);
        return Results.Problem("An error occurred while retrieving the agent");
    }
})
.WithName("GetAgent")
.WithSummary("Get a specific agent")
.WithDescription("Retrieves a specific AI agent by ID for the specified user.")
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
        return Results.Created($"/agents/{agent.Owner}/{created.Id}", created);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error creating agent for owner {Owner}", agent.Owner);
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

app.MapPut("/agents/{owner}/{id}", async (string owner, string id, [FromBody] Agent agent, IAgentStorageService storage) =>
{
    try
    {
        agent.Id = id;
        agent.Owner = owner;
        var updated = await storage.UpdateAgentAsync(agent);
        return Results.Ok(updated);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error updating agent {Id} for owner {Owner}", id, owner);
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

app.MapDelete("/agents/{owner}/{id}", async (string owner, string id, IAgentStorageService storage) =>
{
    try
    {
        await storage.DeleteAgentAsync(id, owner);
        return Results.NoContent();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error deleting agent {Id} for owner {Owner}", id, owner);
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
app.MapPost("/agents/{owner}/{id}/run", async (
    string owner,
    string id,
    [FromBody] Dictionary<string, string> inputValues,
    IAgentExecutionService executionService) =>
{
    try
    {
        var run = await executionService.ExecuteAgentAsync(id, owner, inputValues);
        return Results.Ok(run);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error executing agent {Id} for owner {Owner}", id, owner);
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

app.MapGet("/runs/{owner}/{id}", async (string owner, string id, IAgentStorageService storage) =>
{
    try
    {
        var run = await storage.GetRunAsync(id, owner);
        if (run == null)
        {
            return Results.NotFound($"Run {id} not found");
        }
        return Results.Ok(run);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error retrieving run {Id} for owner {Owner}", id, owner);
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

app.MapGet("/agents/{owner}/{agentId}/runs", async (string owner, string agentId, IAgentStorageService storage) =>
{
    try
    {
        var runs = await storage.GetRunsByAgentAsync(agentId, owner);
        return Results.Ok(runs);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error retrieving runs for agent {AgentId} and owner {Owner}", agentId, owner);
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

app.Run();
