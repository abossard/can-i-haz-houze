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
    [JsonProperty(PropertyName = "agentId")]
    public string AgentId { get; set; } = string.Empty;
    
    // Entity type discriminator for mixed collection
    [JsonProperty(PropertyName = "entityType")]
    public string EntityType { get; set; } = "agent-run";
    
    [JsonProperty(PropertyName = "inputValues")]
    public Dictionary<string, string> InputValues { get; set; } = new();
    
    [JsonProperty(PropertyName = "status")]
    public string Status { get; set; } = "pending"; // pending, running, paused, completed, failed
    
    [JsonProperty(PropertyName = "result")]
    public string? Result { get; set; }
    
    [JsonProperty(PropertyName = "error")]
    public string? Error { get; set; }
    
    [JsonProperty(PropertyName = "logs")]
    public List<AgentRunLog> Logs { get; set; } = new();
    
    [JsonProperty(PropertyName = "conversationHistory")]
    public List<ConversationTurn> ConversationHistory { get; set; } = new();
    
    [JsonProperty(PropertyName = "turnCount")]
    public int TurnCount { get; set; } = 0;
    
    [JsonProperty(PropertyName = "maxTurns")]
    public int MaxTurns { get; set; } = 10;
    
    [JsonProperty(PropertyName = "goal")]
    public string? Goal { get; set; }
    
    [JsonProperty(PropertyName = "goalAchieved")]
    public bool GoalAchieved { get; set; } = false;
    
    [JsonProperty(PropertyName = "startedAt")]
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    
    [JsonProperty(PropertyName = "pausedAt")]
    public DateTime? PausedAt { get; set; }
    
    [JsonProperty(PropertyName = "completedAt")]
    public DateTime? CompletedAt { get; set; }
    
    [JsonProperty(PropertyName = "lastUpdated")]
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

public class ConversationTurn
{
    [JsonProperty(PropertyName = "turnNumber")]
    public int TurnNumber { get; set; }
    
    [JsonProperty(PropertyName = "role")]
    public string Role { get; set; } = string.Empty; // user, assistant, system, tool
    
    [JsonProperty(PropertyName = "content")]
    public string Content { get; set; } = string.Empty;
    
    [JsonProperty(PropertyName = "timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    [JsonProperty(PropertyName = "metadata")]
    public Dictionary<string, object>? Metadata { get; set; }
    
    [JsonProperty(PropertyName = "toolCalls")]
    public List<ToolCall>? ToolCalls { get; set; }
    
    [JsonProperty(PropertyName = "toolCallId")]
    public string? ToolCallId { get; set; }
    
    [JsonProperty(PropertyName = "toolName")]
    public string? ToolName { get; set; }
}

public class ToolCall
{
    [JsonProperty(PropertyName = "id")]
    public string Id { get; set; } = string.Empty;
    
    [JsonProperty(PropertyName = "name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonProperty(PropertyName = "arguments")]
    public string Arguments { get; set; } = string.Empty;
    
    [JsonProperty(PropertyName = "result")]
    public string? Result { get; set; }
}

public class AgentRunLog
{
    [JsonProperty(PropertyName = "timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    [JsonProperty(PropertyName = "level")]
    public string Level { get; set; } = "info";
    
    [JsonProperty(PropertyName = "message")]
    public string Message { get; set; } = string.Empty;
    
    [JsonProperty(PropertyName = "data")]
    public Dictionary<string, object>? Data { get; set; }
}
