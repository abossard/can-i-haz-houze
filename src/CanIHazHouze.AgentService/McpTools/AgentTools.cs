using System.ComponentModel;
using ModelContextProtocol.Server;
using CanIHazHouze.AgentService.Services;
using CanIHazHouze.AgentService.Models;

namespace CanIHazHouze.AgentService.McpTools;

/// <summary>
/// MCP tools for managing AI agents and their execution
/// </summary>
[McpServerToolType]
public class AgentTools
{
    private readonly IAgentStorageService _storageService;
    private readonly IAgentExecutionService _executionService;
    private readonly ILogger<AgentTools> _logger;

    public AgentTools(
        IAgentStorageService storageService,
        IAgentExecutionService executionService,
        ILogger<AgentTools> logger)
    {
        _storageService = storageService;
        _executionService = executionService;
        _logger = logger;
    }

    [McpServerTool]
    [Description("List all available AI agents")]
    public async Task<object> ListAgents()
    {
        try
        {
            var agents = await _storageService.GetAllAgentsAsync();
            return agents.Select(a => new
            {
                id = a.Id,
                name = a.Name,
                description = a.Description,
                model = a.Config.Model,
                tools = a.Tools,
                created = a.CreatedAt
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing agents via MCP");
            return new { error = ex.Message };
        }
    }

    [McpServerTool]
    [Description("Get details of a specific AI agent")]
    public async Task<object> GetAgent(
        [Description("The ID of the agent to retrieve")] string agentId)
    {
        try
        {
            var agent = await _storageService.GetAgentAsync(agentId);
            if (agent == null)
            {
                return new { error = $"Agent {agentId} not found" };
            }

            return new
            {
                id = agent.Id,
                name = agent.Name,
                description = agent.Description,
                prompt = agent.Prompt,
                model = agent.Config.Model,
                temperature = agent.Config.Temperature,
                tools = agent.Tools,
                inputVariables = agent.InputVariables,
                created = agent.CreatedAt,
                updated = agent.UpdatedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting agent {AgentId} via MCP", agentId);
            return new { error = ex.Message };
        }
    }

    [McpServerTool]
    [Description("Execute an AI agent with input values")]
    public async Task<object> ExecuteAgent(
        [Description("The ID of the agent to execute")] string agentId,
        [Description("JSON string containing input variable key-value pairs")] string inputValuesJson)
    {
        try
        {
            var inputValues = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(inputValuesJson);
            if (inputValues == null)
            {
                return new { error = "Invalid input values JSON" };
            }

            var run = await _executionService.ExecuteAgentAsync(agentId, inputValues);
            
            return new
            {
                runId = run.Id,
                agentId = run.AgentId,
                status = run.Status,
                result = run.Result,
                error = run.Error,
                started = run.StartedAt,
                completed = run.CompletedAt,
                logs = run.Logs.Select(l => new
                {
                    timestamp = l.Timestamp,
                    level = l.Level,
                    message = l.Message
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing agent {AgentId} via MCP", agentId);
            return new { error = ex.Message };
        }
    }

    [McpServerTool]
    [Description("Get the status and results of an agent run")]
    public async Task<object> GetRun(
        [Description("The ID of the agent")] string agentId,
        [Description("The ID of the run")] string runId)
    {
        try
        {
            var run = await _storageService.GetRunAsync(runId, agentId);
            if (run == null)
            {
                return new { error = $"Run {runId} not found for agent {agentId}" };
            }

            return new
            {
                runId = run.Id,
                agentId = run.AgentId,
                status = run.Status,
                result = run.Result,
                error = run.Error,
                started = run.StartedAt,
                completed = run.CompletedAt,
                inputValues = run.InputValues,
                conversationHistory = run.ConversationHistory.Select(t => new
                {
                    turnNumber = t.TurnNumber,
                    role = t.Role,
                    content = t.Content,
                    timestamp = t.Timestamp
                }).ToList(),
                logs = run.Logs.Select(l => new
                {
                    timestamp = l.Timestamp,
                    level = l.Level,
                    message = l.Message
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting run {RunId} via MCP", runId);
            return new { error = ex.Message };
        }
    }

    [McpServerTool]
    [Description("List all runs for a specific agent")]
    public async Task<object> ListAgentRuns(
        [Description("The ID of the agent")] string agentId)
    {
        try
        {
            var runs = await _storageService.GetRunsByAgentAsync(agentId);
            return runs.Select(r => new
            {
                runId = r.Id,
                agentId = r.AgentId,
                status = r.Status,
                started = r.StartedAt,
                completed = r.CompletedAt,
                hasResult = !string.IsNullOrEmpty(r.Result),
                hasError = !string.IsNullOrEmpty(r.Error)
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing runs for agent {AgentId} via MCP", agentId);
            return new { error = ex.Message };
        }
    }

    [McpServerTool]
    [Description("Continue a chat conversation with a completed agent run")]
    public async Task<object> ContinueChat(
        [Description("The ID of the agent")] string agentId,
        [Description("The ID of the run to continue")] string runId,
        [Description("The user message to send")] string message)
    {
        try
        {
            var run = await _executionService.ContinueChatAsync(agentId, runId, message);
            
            return new
            {
                runId = run.Id,
                agentId = run.AgentId,
                status = run.Status,
                conversationHistory = run.ConversationHistory.Select(t => new
                {
                    turnNumber = t.TurnNumber,
                    role = t.Role,
                    content = t.Content,
                    timestamp = t.Timestamp
                }).ToList(),
                lastUpdated = run.LastUpdated
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error continuing chat for run {RunId} via MCP", runId);
            return new { error = ex.Message };
        }
    }
}
