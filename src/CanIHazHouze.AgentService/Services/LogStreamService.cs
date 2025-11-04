using CanIHazHouze.AgentService.Hubs;
using CanIHazHouze.AgentService.Models;
using Microsoft.AspNetCore.SignalR;

namespace CanIHazHouze.AgentService.Services;

/// <summary>
/// Simple service for broadcasting agent events via SignalR
/// </summary>
public interface IAgentEventBroadcaster
{
    Task BroadcastLogAsync(string runId, string agentId, AgentRunLog log);
    Task BroadcastConversationAsync(string runId, string agentId, ConversationTurn turn);
}

public class AgentEventBroadcaster : IAgentEventBroadcaster
{
    private readonly IHubContext<AgentHub> _hubContext;
    private readonly ILogger<AgentEventBroadcaster> _logger;

    public AgentEventBroadcaster(IHubContext<AgentHub> hubContext, ILogger<AgentEventBroadcaster> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task BroadcastLogAsync(string runId, string agentId, AgentRunLog log)
    {
        _logger.LogInformation("Broadcasting log for run {RunId}, agent {AgentId}: {Message}", runId, agentId, log.Message);
        await _hubContext.Clients.All.SendAsync("Log", new { runId, agentId, log });
    }

    public async Task BroadcastConversationAsync(string runId, string agentId, ConversationTurn turn)
    {
        _logger.LogInformation("Broadcasting conversation for run {RunId}, agent {AgentId}, turn {Turn}, role {Role}, content length {Length}", 
            runId, agentId, turn.TurnNumber, turn.Role, turn.Content?.Length ?? 0);
        
        try
        {
            await _hubContext.Clients.All.SendAsync("Conversation", new { runId, agentId, turn });
            _logger.LogInformation("Successfully broadcast conversation");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast conversation");
        }
    }
}
