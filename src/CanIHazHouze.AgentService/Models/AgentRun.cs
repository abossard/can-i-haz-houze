using System.ComponentModel.DataAnnotations;

namespace CanIHazHouze.AgentService.Models;

public class AgentRun
{
    [Required]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    [Required]
    public string AgentId { get; set; } = string.Empty;
    
    public Dictionary<string, string> InputValues { get; set; } = new();
    
    public string Status { get; set; } = "pending";
    
    public string? Result { get; set; }
    
    public string? Error { get; set; }
    
    public List<AgentRunLog> Logs { get; set; } = new();
    
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? CompletedAt { get; set; }
}

public class AgentRunLog
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    public string Level { get; set; } = "info";
    
    public string Message { get; set; } = string.Empty;
    
    public Dictionary<string, object>? Data { get; set; }
}
