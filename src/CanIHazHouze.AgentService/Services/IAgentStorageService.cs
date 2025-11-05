using CanIHazHouze.AgentService.Models;

namespace CanIHazHouze.AgentService.Services;

public interface IAgentStorageService
{
    Task<Agent> CreateAgentAsync(Agent agent);
    Task<Agent?> GetAgentAsync(string id);
    Task<List<Agent>> GetAllAgentsAsync();
    Task<Agent> UpdateAgentAsync(Agent agent);
    Task DeleteAgentAsync(string id);
    
    Task<AgentRun> CreateRunAsync(AgentRun run);
    Task<AgentRun?> GetRunAsync(string id, string agentId);
    Task<List<AgentRun>> GetRunsByAgentAsync(string agentId);
    Task<AgentRun> UpdateRunAsync(AgentRun run);
    Task DeleteRunAsync(string id, string agentId);
    Task<int> DeleteAllRunsAsync(string agentId);
}
