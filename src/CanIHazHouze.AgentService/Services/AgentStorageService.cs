using CanIHazHouze.AgentService.Models;
using CanIHazHouze.AgentService.Security;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace CanIHazHouze.AgentService.Services;

public class AgentStorageOptions
{
    public string DatabaseName { get; set; } = "houze";
    public string AgentContainerName { get; set; } = "agents";
    public string RunContainerName { get; set; } = "agent-runs";
}

public class AgentStorageService : IAgentStorageService
{
    private readonly CosmosClient _cosmosClient;
    private readonly AgentStorageOptions _options;
    private readonly ILogger<AgentStorageService> _logger;
    private Container? _agentContainer;
    private Container? _runContainer;

    public AgentStorageService(
        CosmosClient cosmosClient,
        IOptions<AgentStorageOptions> options,
        ILogger<AgentStorageService> logger)
    {
        _cosmosClient = cosmosClient;
        _options = options.Value;
        _logger = logger;
    }

    private Task<Container> GetAgentContainerAsync()
    {
        if (_agentContainer == null)
        {
            var database = _cosmosClient.GetDatabase(_options.DatabaseName);
            _agentContainer = database.GetContainer(_options.AgentContainerName);
        }
        return Task.FromResult(_agentContainer);
    }

    private Task<Container> GetRunContainerAsync()
    {
        if (_runContainer == null)
        {
            var database = _cosmosClient.GetDatabase(_options.DatabaseName);
            _runContainer = database.GetContainer(_options.RunContainerName);
        }
        return Task.FromResult(_runContainer);
    }

    public async Task<Agent> CreateAgentAsync(Agent agent)
    {
        var container = await GetAgentContainerAsync();
        agent.CreatedAt = DateTime.UtcNow;
        agent.UpdatedAt = DateTime.UtcNow;
        var response = await container.CreateItemAsync(agent, new PartitionKey(agent.Id));
        _logger.LogInformation("Created agent {AgentId}", LogSanitizer.Sanitize(agent.Id));
        return response.Resource;
    }

    public async Task<Agent?> GetAgentAsync(string id)
    {
        try
        {
            var container = await GetAgentContainerAsync();
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
        var container = await GetAgentContainerAsync();
        var query = new QueryDefinition("SELECT * FROM c");
        
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
        var container = await GetAgentContainerAsync();
        agent.UpdatedAt = DateTime.UtcNow;
        var response = await container.ReplaceItemAsync(agent, agent.Id, new PartitionKey(agent.Id));
        _logger.LogInformation("Updated agent {AgentId}", LogSanitizer.Sanitize(agent.Id));
        return response.Resource;
    }

    public async Task DeleteAgentAsync(string id)
    {
        var container = await GetAgentContainerAsync();
        await container.DeleteItemAsync<Agent>(id, new PartitionKey(id));
        _logger.LogInformation("Deleted agent {AgentId}", LogSanitizer.Sanitize(id));
    }

    public async Task<AgentRun> CreateRunAsync(AgentRun run)
    {
        var container = await GetRunContainerAsync();
        run.StartedAt = DateTime.UtcNow;
        var response = await container.CreateItemAsync(run, new PartitionKey(run.AgentId));
        _logger.LogInformation("Created run {RunId} for agent {AgentId}", LogSanitizer.Sanitize(run.Id), LogSanitizer.Sanitize(run.AgentId));
        return response.Resource;
    }

    public async Task<AgentRun?> GetRunAsync(string id, string agentId)
    {
        try
        {
            var container = await GetRunContainerAsync();
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
        var container = await GetRunContainerAsync();
        var query = new QueryDefinition("SELECT * FROM c WHERE c.agentId = @agentId ORDER BY c.startedAt DESC")
            .WithParameter("@agentId", agentId);
        
        var iterator = container.GetItemQueryIterator<AgentRun>(query);
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
        var container = await GetRunContainerAsync();
        var response = await container.ReplaceItemAsync(run, run.Id, new PartitionKey(run.AgentId));
        _logger.LogInformation("Updated run {RunId} for agent {AgentId}", LogSanitizer.Sanitize(run.Id), LogSanitizer.Sanitize(run.AgentId));
        return response.Resource;
    }
}
