using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json; // Cosmos SDK uses Newtonsoft serialization

namespace CanIHazHouze.AgentService.Models;

public class AgentRun
{
    // Ensure Cosmos stores run documents with required lowercase 'id'
    [Required]
    [JsonProperty(PropertyName = "id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    [Required]
    public string AgentId { get; set; } = string.Empty;
    
    // Entity type discriminator for mixed collection
    public string EntityType { get; set; } = "agent-run";
    
    public Dictionary<string, string> InputValues { get; set; } = new();
    
    public string Status { get; set; } = "pending"; // pending, running, paused, completed, failed
    
    public string? Result { get; set; }
    
    public string? Error { get; set; }
    
    public List<AgentRunLog> Logs { get; set; } = new();
    
    public List<ConversationTurn> ConversationHistory { get; set; } = new();
    
    public int TurnCount { get; set; } = 0;
    
    public int MaxTurns { get; set; } = 10;
    
    public string? Goal { get; set; }
    
    public bool GoalAchieved { get; set; } = false;
    
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? PausedAt { get; set; }
    
    public DateTime? CompletedAt { get; set; }
    
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

public class ConversationTurn
{
    public int TurnNumber { get; set; }
    
    public string Role { get; set; } = string.Empty; // user, assistant, system, tool
    
    public string Content { get; set; } = string.Empty;
    
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    public Dictionary<string, object>? Metadata { get; set; }
    
    public List<ToolCall>? ToolCalls { get; set; }
    
    public string? ToolCallId { get; set; }
    
    public string? ToolName { get; set; }
}

public class ToolCall
{
    public string Id { get; set; } = string.Empty;
    
    public string Name { get; set; } = string.Empty;
    
    public string Arguments { get; set; } = string.Empty;
    
    public string? Result { get; set; }
}

public class AgentRunLog
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    public string Level { get; set; } = "info";
    
    public string Message { get; set; } = string.Empty;
    
    public Dictionary<string, object>? Data { get; set; }
}
