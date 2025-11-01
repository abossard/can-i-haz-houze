using CanIHazHouze.AgentService.Models;

namespace CanIHazHouze.AgentService.Services;

public interface IAgentExecutionService
{
    Task<AgentRun> ExecuteAgentAsync(string agentId, Dictionary<string, string> inputValues);
}
