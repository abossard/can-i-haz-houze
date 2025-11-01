using CanIHazHouze.AgentService.Models;

namespace CanIHazHouze.AgentService.Services;

public interface IAgentExecutionService
{
    Task<AgentRun> ExecuteAgentAsync(string agentId, string owner, Dictionary<string, string> inputValues);
}
