using CanIHazHouze.AgentService.Models;

namespace CanIHazHouze.AgentService.Services;

public interface IAgentStorageService
{
    Task<Agent> CreateAgentAsync(Agent agent);
    Task<Agent?> GetAgentAsync(string id, string owner);
    Task<List<Agent>> GetAgentsByOwnerAsync(string owner);
    Task<Agent> UpdateAgentAsync(Agent agent);
    Task DeleteAgentAsync(string id, string owner);
    
    Task<AgentRun> CreateRunAsync(AgentRun run);
    Task<AgentRun?> GetRunAsync(string id, string owner);
    Task<List<AgentRun>> GetRunsByAgentAsync(string agentId, string owner);
    Task<AgentRun> UpdateRunAsync(AgentRun run);
}
