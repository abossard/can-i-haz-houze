using CanIHazHouze.AgentService.Models;
using CanIHazHouze.AgentService.Security;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace CanIHazHouze.AgentService.Services;

public class AgentStorageOptions
{
    public string DatabaseName { get; set; } = "houze";
    public string ContainerName { get; set; } = "agents";
}

public class AgentStorageService : IAgentStorageService
{
    private readonly CosmosClient _cosmosClient;
    private readonly AgentStorageOptions _options;
    private readonly ILogger<AgentStorageService> _logger;
    private Container? _container;

    public AgentStorageService(
        CosmosClient cosmosClient,
        IOptions<AgentStorageOptions> options,
        ILogger<AgentStorageService> logger)
    {
        _cosmosClient = cosmosClient;
        _options = options.Value;
        _logger = logger;
    }

    private Task<Container> GetContainerAsync()
    {
        if (_container == null)
        {
            var database = _cosmosClient.GetDatabase(_options.DatabaseName);
            _container = database.GetContainer(_options.ContainerName);
        }
        return Task.FromResult(_container);
    }

    public async Task<Agent> CreateAgentAsync(Agent agent)
    {
        var container = await GetContainerAsync();
        agent.AgentId = agent.Id; // Set partition key to agent's own ID
        agent.EntityType = "agent";
        agent.CreatedAt = DateTime.UtcNow;
        agent.UpdatedAt = DateTime.UtcNow;
        var response = await container.CreateItemAsync(agent, new PartitionKey(agent.AgentId));
        _logger.LogInformation("Created agent {AgentId}", LogSanitizer.Sanitize(agent.Id));
        return response.Resource;
    }

    public async Task<Agent?> GetAgentAsync(string id)
    {
        try
        {
            var container = await GetContainerAsync();
            var response = await container.ReadItemAsync<Agent>(id, new PartitionKey(id));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<List<Agent>> GetAllAgentsAsync()
    {
        var container = await GetContainerAsync();
        var query = new QueryDefinition("SELECT * FROM c WHERE c.entityType = @entityType")
            .WithParameter("@entityType", "agent");
        
        var iterator = container.GetItemQueryIterator<Agent>(query);
        var agents = new List<Agent>();
        
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            agents.AddRange(response);
        }
        
        return agents;
    }

    public async Task<Agent> UpdateAgentAsync(Agent agent)
    {
        var container = await GetContainerAsync();
        agent.AgentId = agent.Id; // Ensure partition key is set
        agent.UpdatedAt = DateTime.UtcNow;
        var response = await container.ReplaceItemAsync(agent, agent.Id, new PartitionKey(agent.AgentId));
        _logger.LogInformation("Updated agent {AgentId}", LogSanitizer.Sanitize(agent.Id));
        return response.Resource;
    }

    public async Task DeleteAgentAsync(string id)
    {
        var container = await GetContainerAsync();
        await container.DeleteItemAsync<Agent>(id, new PartitionKey(id));
        _logger.LogInformation("Deleted agent {AgentId}", LogSanitizer.Sanitize(id));
    }

    public async Task<AgentRun> CreateRunAsync(AgentRun run)
    {
        var container = await GetContainerAsync();
        run.EntityType = "agent-run";
        run.StartedAt = DateTime.UtcNow;
        var response = await container.CreateItemAsync(run, new PartitionKey(run.AgentId));
        _logger.LogInformation("Created run {RunId} for agent {AgentId}", LogSanitizer.Sanitize(run.Id), LogSanitizer.Sanitize(run.AgentId));
        return response.Resource;
    }

    public async Task<AgentRun?> GetRunAsync(string id, string agentId)
    {
        try
        {
            var container = await GetContainerAsync();
            var response = await container.ReadItemAsync<AgentRun>(id, new PartitionKey(agentId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<List<AgentRun>> GetRunsByAgentAsync(string agentId)
    {
        var container = await GetContainerAsync();
        var query = new QueryDefinition("SELECT * FROM c WHERE c.agentId = @agentId AND c.entityType = @entityType ORDER BY c.startedAt DESC")
            .WithParameter("@agentId", agentId)
            .WithParameter("@entityType", "agent-run");
        
        var iterator = container.GetItemQueryIterator<AgentRun>(query, requestOptions: new QueryRequestOptions 
        { 
            PartitionKey = new PartitionKey(agentId) 
        });
        var runs = new List<AgentRun>();
        
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            runs.AddRange(response);
        }
        
        return runs;
    }

    public async Task<AgentRun> UpdateRunAsync(AgentRun run)
    {
        var container = await GetContainerAsync();
        var response = await container.ReplaceItemAsync(run, run.Id, new PartitionKey(run.AgentId));
        _logger.LogInformation("Updated run {RunId} for agent {AgentId}", LogSanitizer.Sanitize(run.Id), LogSanitizer.Sanitize(run.AgentId));
        return response.Resource;
    }

    public async Task DeleteRunAsync(string id, string agentId)
    {
        var container = await GetContainerAsync();
        await container.DeleteItemAsync<AgentRun>(id, new PartitionKey(agentId));
        _logger.LogInformation("Deleted run {RunId} for agent {AgentId}", LogSanitizer.Sanitize(id), LogSanitizer.Sanitize(agentId));
    }

    public async Task<int> DeleteAllRunsAsync(string agentId)
    {
        var container = await GetContainerAsync();
        var query = new QueryDefinition("SELECT c.id FROM c WHERE c.agentId = @agentId AND c.entityType = @entityType")
            .WithParameter("@agentId", agentId)
            .WithParameter("@entityType", "agent-run");
        
        var iterator = container.GetItemQueryIterator<dynamic>(query, requestOptions: new QueryRequestOptions 
        { 
            PartitionKey = new PartitionKey(agentId) 
        });
        
        var runIds = new List<string>();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            foreach (var item in response)
            {
                runIds.Add((string)item.id);
            }
        }

        foreach (var runId in runIds)
        {
            await container.DeleteItemAsync<AgentRun>(runId, new PartitionKey(agentId));
        }

        _logger.LogInformation("Deleted {Count} runs for agent {AgentId}", runIds.Count, LogSanitizer.Sanitize(agentId));
        return runIds.Count;
    }
}
