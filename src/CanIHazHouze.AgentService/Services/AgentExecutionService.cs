using CanIHazHouze.AgentService.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace CanIHazHouze.AgentService.Services;

public class AgentExecutionService : IAgentExecutionService
{
    private readonly IAgentStorageService _storageService;
    private readonly Kernel _kernel;
    private readonly ILogger<AgentExecutionService> _logger;

    public AgentExecutionService(
        IAgentStorageService storageService,
        Kernel kernel,
        ILogger<AgentExecutionService> logger)
    {
        _storageService = storageService;
        _kernel = kernel;
        _logger = logger;
    }

    public async Task<AgentRun> ExecuteAgentAsync(string agentId, string owner, Dictionary<string, string> inputValues)
    {
        var run = new AgentRun
        {
            AgentId = agentId,
            Owner = owner,
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

            var agent = await _storageService.GetAgentAsync(agentId, owner);
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
                    { "temperature", agent.Config.Temperature },
                    { "topP", agent.Config.TopP },
                    { "maxTokens", agent.Config.MaxTokens }
                }
            });

            // Execute the agent
            var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage(prompt);

            run.Logs.Add(new AgentRunLog
            {
                Level = "info",
                Message = "Sending request to AI model"
            });

            var result = await chatCompletion.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                _kernel);

            run.Result = result.Content;
            run.Status = "completed";
            run.CompletedAt = DateTime.UtcNow;

            run.Logs.Add(new AgentRunLog
            {
                Level = "info",
                Message = "Agent execution completed successfully"
            });

            _logger.LogInformation("Agent {AgentId} executed successfully for run {RunId}", agentId, run.Id);
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

            _logger.LogError(ex, "Agent {AgentId} execution failed for run {RunId}", agentId, run.Id);
        }

        await _storageService.UpdateRunAsync(run);
        return run;
    }
}
