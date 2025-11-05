using Microsoft.AspNetCore.SignalR;

namespace CanIHazHouze.AgentService.Hubs;

/// <summary>
/// SignalR hub for broadcasting agent execution events to all connected clients
/// </summary>
public class AgentHub : Hub
{
    // Simple hub - no methods needed, just broadcasts
}
