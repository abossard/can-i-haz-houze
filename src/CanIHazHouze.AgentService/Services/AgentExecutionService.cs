using CanIHazHouze.AgentService.Configuration;
using CanIHazHouze.AgentService.Models;
using CanIHazHouze.AgentService.Security;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace CanIHazHouze.AgentService.Services;

public class AgentExecutionService : IAgentExecutionService
{
    private readonly IAgentStorageService _storageService;
    private readonly Azure.AI.OpenAI.AzureOpenAIClient _openAIClient;
    private readonly IMcpClientService _mcpClientService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AgentExecutionService> _logger;

    public AgentExecutionService(
        IAgentStorageService storageService,
        Azure.AI.OpenAI.AzureOpenAIClient openAIClient,
        IMcpClientService mcpClientService,
        IConfiguration configuration,
        ILogger<AgentExecutionService> logger)
    {
        _storageService = storageService;
        _openAIClient = openAIClient;
        _mcpClientService = mcpClientService;
        _configuration = configuration;
        _logger = logger;
    }
    
    private async Task<Kernel> CreateKernelForModelAsync(string deploymentName, List<string> tools, CancellationToken cancellationToken)
    {
        var builder = Kernel.CreateBuilder();
        
        // Use the shared AzureOpenAIClient which already has DefaultAzureCredential configured
        builder.AddAzureOpenAIChatCompletion(
            deploymentName: deploymentName,
            azureOpenAIClient: _openAIClient);
        
        // Map of tool names to their MCP endpoints (from service discovery)
        var mcpEndpoints = new Dictionary<string, string>
        {
            ["ledgerapi"] = _configuration.GetConnectionString("ledgerservice") + "/mcp",
            ["crmapi"] = _configuration.GetConnectionString("crmservice") + "/mcp",
            ["documentsapi"] = _configuration.GetConnectionString("documentservice") + "/mcp",
            // Add more services as needed
        };
        
        // Register MCP plugins based on agent's tool configuration
        foreach (var tool in tools)
        {
            var toolKey = tool.ToLowerInvariant();
            
            if (mcpEndpoints.TryGetValue(toolKey, out var mcpEndpoint))
            {
                try
                {
                    await builder.AddMcpToolsAsync(
                        _mcpClientService,
                        mcpEndpoint,
                        tool, // Use original casing for plugin name
                        _logger,
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load MCP tools for {Tool} from {Endpoint}", tool, mcpEndpoint);
                }
            }
            else
            {
                _logger.LogWarning("Unknown tool or no MCP endpoint configured: {Tool}", tool);
            }
        }
        
        return builder.Build();
    }

    public async Task<AgentRun> ExecuteAgentAsync(string agentId, Dictionary<string, string> inputValues)
    {
        var run = new AgentRun
        {
            AgentId = agentId,
            InputValues = inputValues,
            Status = "running"
        };

        try
        {
            run = await _storageService.CreateRunAsync(run);
            run.Logs.Add(new AgentRunLog
            {
                Level = "info",
                Message = "Agent execution started"
            });

            var agent = await _storageService.GetAgentAsync(agentId);
            if (agent == null)
            {
                throw new InvalidOperationException($"Agent {agentId} not found");
            }

            run.Logs.Add(new AgentRunLog
            {
                Level = "info",
                Message = $"Loaded agent: {agent.Name}"
            });

            // Validate required input variables
            foreach (var inputVar in agent.InputVariables.Where(v => v.Required))
            {
                if (!inputValues.ContainsKey(inputVar.Name) || string.IsNullOrWhiteSpace(inputValues[inputVar.Name]))
                {
                    throw new InvalidOperationException($"Required input variable '{inputVar.Name}' is missing");
                }
            }

            // Build the prompt with input variables
            var prompt = agent.Prompt;
            foreach (var kvp in inputValues)
            {
                prompt = prompt.Replace($"{{{{{kvp.Key}}}}}", kvp.Value);
            }

            run.Logs.Add(new AgentRunLog
            {
                Level = "info",
                Message = "Prompt prepared with input variables"
            });

            // Configure execution settings
            var executionSettings = new OpenAIPromptExecutionSettings
            {
                Temperature = agent.Config.Temperature,
                TopP = agent.Config.TopP,
                MaxTokens = agent.Config.MaxTokens,
                FrequencyPenalty = agent.Config.FrequencyPenalty,
                PresencePenalty = agent.Config.PresencePenalty
            };

            run.Logs.Add(new AgentRunLog
            {
                Level = "info",
                Message = "Execution settings configured",
                Data = new Dictionary<string, object>
                {
                    { "model", agent.Config.Model },
                    { "temperature", agent.Config.Temperature },
                    { "topP", agent.Config.TopP },
                    { "maxTokens", agent.Config.MaxTokens }
                }
            });

            // Create kernel with the agent's specified model deployment and tools
            var kernel = await CreateKernelForModelAsync(agent.Config.Model, agent.Tools, CancellationToken.None);
            
            // Enable automatic function calling if tools are configured
            if (agent.Tools.Any())
            {
                executionSettings.ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions;
                
                run.Logs.Add(new AgentRunLog
                {
                    Level = "info",
                    Message = $"Enabled automatic tool calling for: {string.Join(", ", agent.Tools)}"
                });
            }
            
            // Execute the agent
            var chatCompletion = kernel.GetRequiredService<IChatCompletionService>();
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage(prompt);

            run.Logs.Add(new AgentRunLog
            {
                Level = "info",
                Message = $"Sending request to AI model: {agent.Config.Model}"
            });

            var result = await chatCompletion.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                kernel);

            run.Result = result.Content;
            run.Status = "completed";
            run.CompletedAt = DateTime.UtcNow;

            run.Logs.Add(new AgentRunLog
            {
                Level = "info",
                Message = "Agent execution completed successfully"
            });

            _logger.LogInformation("Agent {AgentId} executed successfully for run {RunId}", LogSanitizer.Sanitize(agentId), LogSanitizer.Sanitize(run.Id));
        }
        catch (Exception ex)
        {
            run.Status = "failed";
            run.Error = ex.Message;
            run.CompletedAt = DateTime.UtcNow;
            
            run.Logs.Add(new AgentRunLog
            {
                Level = "error",
                Message = $"Agent execution failed: {ex.Message}"
            });

            _logger.LogError(ex, "Agent {AgentId} execution failed for run {RunId}", LogSanitizer.Sanitize(agentId), LogSanitizer.Sanitize(run.Id));
        }

        await _storageService.UpdateRunAsync(run);
        return run;
    }
}
