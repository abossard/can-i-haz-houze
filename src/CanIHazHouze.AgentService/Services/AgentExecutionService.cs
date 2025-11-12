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
    private readonly IAgentEventBroadcaster _broadcaster;
    private readonly IHttpClientFactory _httpClientFactory;

    public AgentExecutionService(
        IAgentStorageService storageService,
        Azure.AI.OpenAI.AzureOpenAIClient openAIClient,
        IMcpClientService mcpClientService,
        IConfiguration configuration,
        ILogger<AgentExecutionService> logger,
        IAgentEventBroadcaster broadcaster,
        IHttpClientFactory httpClientFactory)
    {
        _storageService = storageService;
        _openAIClient = openAIClient;
        _mcpClientService = mcpClientService;
        _configuration = configuration;
        _logger = logger;
        _broadcaster = broadcaster;
        _httpClientFactory = httpClientFactory;
    }
    
    private async Task AddLogAsync(AgentRun run, AgentRunLog log)
    {
        run.Logs.Add(log);
        await _broadcaster.BroadcastLogAsync(run.Id, run.AgentId, log);
    }
    
    private async Task<Kernel> CreateKernelForModelAsync(string deploymentName, List<string> tools, AgentRun run, CancellationToken cancellationToken)
    {
        var builder = Kernel.CreateBuilder();
        
        // Add function invocation filter to log all tool calls
        builder.Services.AddSingleton<IFunctionInvocationFilter>(new FunctionInvocationLoggingFilter(run, _logger, _broadcaster));
        
        // Use the shared AzureOpenAIClient which already has DefaultAzureCredential configured
        builder.AddAzureOpenAIChatCompletion(
            deploymentName: deploymentName,
            azureOpenAIClient: _openAIClient);
        
        // Map of tool names to their service names (case-insensitive)
        var serviceMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ledgerapi"] = "ledgerservice",
            ["crmapi"] = "crmservice",
            ["documentsapi"] = "documentservice",
            ["agentapi"] = "agentservice",
            ["agentworkbench"] = "agentservice"
        };
        
        // Register MCP plugins based on agent's tool configuration
        foreach (var tool in tools)
        {
            var toolKey = tool;
            
            if (serviceMap.TryGetValue(toolKey, out var serviceName))
            {
                string? serviceUrl;
                
                // Special handling for self-reference (agentservice)
                if (serviceName.Equals("agentservice", StringComparison.OrdinalIgnoreCase))
                {
                    // Use local base URL for self-reference to avoid circular dependency
                    var urls = _configuration["ASPNETCORE_URLS"] ?? _configuration["urls"];
                    if (!string.IsNullOrEmpty(urls))
                    {
                        // ASPNETCORE_URLS can be multiple URLs separated by semicolons
                        serviceUrl = urls.Split(';')[0];
                    }
                    else
                    {
                        run.Logs.Add(new AgentRunLog
                        {
                            Level = "warning",
                            Message = $"Cannot reference own MCP tools - self-reference not supported. Skipping '{tool}'."
                        });
                        continue;
                    }
                }
                else
                {
                    // Resolve service URL from Aspire configuration
                    // Format: services:servicename:https:0 (injected as services__servicename__https__0 env var)
                    var httpsUrl = _configuration[$"services:{serviceName}:https:0"];
                    var httpUrl = _configuration[$"services:{serviceName}:http:0"];
                    
                    serviceUrl = httpsUrl ?? httpUrl;
                    
                    if (string.IsNullOrEmpty(serviceUrl))
                    {
                        run.Logs.Add(new AgentRunLog
                        {
                            Level = "error",
                            Message = $"Service URL not found for '{serviceName}'. Expected configuration key: services:{serviceName}:https:0"
                        });
                        continue;
                    }
                }
                
                var mcpEndpoint = $"{serviceUrl.TrimEnd('/')}/mcp";
                
                run.Logs.Add(new AgentRunLog
                {
                    Level = "info",
                    Message = $"Resolved MCP endpoint for {tool}: {mcpEndpoint}"
                });
                
                try
                {
                    await builder.AddMcpToolsAsync(
                        _mcpClientService,
                        mcpEndpoint,
                        tool, // Use original casing for plugin name
                        _logger,
                        cancellationToken);
                    
                    run.Logs.Add(new AgentRunLog
                    {
                        Level = "info",
                        Message = $"Successfully loaded MCP plugin: {tool}"
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load MCP tools for {Tool} from {Endpoint}", tool, mcpEndpoint);
                    run.Logs.Add(new AgentRunLog
                    {
                        Level = "error",
                        Message = $"Failed to load MCP tools for {tool}: {ex.Message}"
                    });
                }
            }
            else
            {
                run.Logs.Add(new AgentRunLog
                {
                    Level = "warning",
                    Message = $"Unknown tool or no service mapping configured: {tool}. Available: {string.Join(", ", serviceMap.Keys)}"
                });
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
                Temperature = 0.7,
                MaxTokens = 2000,
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                // Pass MCP tools directly - they implement AIFunction
                // This preserves all parameter metadata from the InputSchema
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
            var kernel = await CreateKernelForModelAsync(agent.Config.Model, agent.Tools, run, CancellationToken.None);
            
            // Log available functions after kernel is built
            var availableFunctions = kernel.Plugins.SelectMany(p => p.Select(f => $"{p.Name}.{f.Name}")).ToList();
            if (availableFunctions.Any())
            {
                // Log function details with parameters for debugging
                var functionsWithParams = kernel.Plugins
                    .SelectMany(p => p.Select(f => new 
                    {
                        name = f.Name,
                        description = f.Description,
                        parameters = f.Metadata.Parameters.Select(param => new
                        {
                            name = param.Name,
                            type = param.ParameterType?.Name ?? "string",
                            required = param.IsRequired,
                            description = param.Description
                        }).ToList()
                    }))
                    .ToList();
                
                run.Logs.Add(new AgentRunLog
                {
                    Level = "info",
                    Message = $"Kernel has {availableFunctions.Count} functions available: {string.Join(", ", availableFunctions)}",
                    Data = new Dictionary<string, object>
                    {
                        { "functions", functionsWithParams }
                    }
                });
            }
            
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

            // Broadcast user message
            var userTurn = new ConversationTurn
            {
                TurnNumber = 0,
                Role = "user",
                Content = prompt,
                Timestamp = DateTime.UtcNow
            };
            run.ConversationHistory.Add(userTurn);
            await _broadcaster.BroadcastConversationAsync(run.Id, run.AgentId, userTurn);

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

            // Broadcast assistant response
            var assistantTurn = new ConversationTurn
            {
                TurnNumber = 1,
                Role = "assistant",
                Content = result.Content ?? "",
                Timestamp = DateTime.UtcNow
            };
            run.ConversationHistory.Add(assistantTurn);
            await _broadcaster.BroadcastConversationAsync(run.Id, run.AgentId, assistantTurn);

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

    public async Task<AgentRun> ContinueChatAsync(string agentId, string runId, string userMessage)
    {
        // Load the completed run
        var run = await _storageService.GetRunAsync(runId, agentId);
        if (run == null)
        {
            throw new InvalidOperationException($"Run {runId} not found");
        }

        if (run.Status != "completed")
        {
            throw new InvalidOperationException($"Can only chat with completed runs. Current status: {run.Status}");
        }

        try
        {
            await AddLogAsync(run, new AgentRunLog
            {
                Level = "info",
                Message = "Chat continuation started"
            });

            // Load the agent to get its configuration
            var agent = await _storageService.GetAgentAsync(agentId);
            if (agent == null)
            {
                throw new InvalidOperationException($"Agent {agentId} not found");
            }

            // Reconstruct ChatHistory from conversation history
            var chatHistory = new ChatHistory();
            foreach (var turn in run.ConversationHistory)
            {
                switch (turn.Role.ToLower())
                {
                    case "system":
                        chatHistory.AddSystemMessage(turn.Content);
                        break;
                    case "user":
                        chatHistory.AddUserMessage(turn.Content);
                        break;
                    case "assistant":
                        chatHistory.AddAssistantMessage(turn.Content);
                        break;
                }
            }

            // Add the new user message
            chatHistory.AddUserMessage(userMessage);
            var userTurn = new ConversationTurn
            {
                TurnNumber = run.ConversationHistory.Count + 1,
                Role = "user",
                Content = userMessage,
                Timestamp = DateTime.UtcNow
            };
            run.ConversationHistory.Add(userTurn);
            await _broadcaster.BroadcastConversationAsync(runId, agentId, userTurn);

            await AddLogAsync(run, new AgentRunLog
            {
                Level = "info",
                Message = $"User message added: {userMessage.Substring(0, Math.Min(50, userMessage.Length))}..."
            });

            // Create kernel with the same tools as original execution
            var kernel = await CreateKernelForModelAsync(agent.Config.Model, agent.Tools, run, CancellationToken.None);

            // Configure execution settings
            var executionSettings = new OpenAIPromptExecutionSettings
            {
                Temperature = agent.Config.Temperature,
                TopP = agent.Config.TopP,
                MaxTokens = agent.Config.MaxTokens,
                FrequencyPenalty = agent.Config.FrequencyPenalty,
                PresencePenalty = agent.Config.PresencePenalty
            };

            // Enable automatic function calling if tools are configured
            if (agent.Tools.Any())
            {
                executionSettings.ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions;
            }

            // Get AI response
            var chatCompletion = kernel.GetRequiredService<IChatCompletionService>();
            var response = await chatCompletion.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                kernel);

            var assistantMessage = response.Content ?? string.Empty;
            chatHistory.AddAssistantMessage(assistantMessage);

            var assistantTurn = new ConversationTurn
            {
                TurnNumber = run.ConversationHistory.Count + 1,
                Role = "assistant",
                Content = assistantMessage,
                Timestamp = DateTime.UtcNow
            };
            run.ConversationHistory.Add(assistantTurn);
            await _broadcaster.BroadcastConversationAsync(runId, agentId, assistantTurn);

            await AddLogAsync(run, new AgentRunLog
            {
                Level = "info",
                Message = $"Assistant response generated ({assistantMessage.Length} chars)"
            });

            // Update the run in storage
            run.LastUpdated = DateTime.UtcNow;
            await _storageService.UpdateRunAsync(run);

            _logger.LogInformation("Chat continued for run {RunId} of agent {AgentId}",
                LogSanitizer.Sanitize(runId),
                LogSanitizer.Sanitize(agentId));

            return run;
        }
        catch (Exception ex)
        {
            await AddLogAsync(run, new AgentRunLog
            {
                Level = "error",
                Message = $"Chat continuation failed: {ex.Message}"
            });

            _logger.LogError(ex, "Chat continuation failed for run {RunId} of agent {AgentId}",
                LogSanitizer.Sanitize(runId),
                LogSanitizer.Sanitize(agentId));

            throw;
        }
    }
}

/// <summary>
/// Filter that logs all function invocations (tool calls) to the agent run logs
/// </summary>
internal class FunctionInvocationLoggingFilter : IFunctionInvocationFilter
{
    private readonly AgentRun _run;
    private readonly ILogger _logger;
    private readonly CanIHazHouze.AgentService.Services.IAgentEventBroadcaster _broadcaster;

    public FunctionInvocationLoggingFilter(AgentRun run, ILogger logger, CanIHazHouze.AgentService.Services.IAgentEventBroadcaster broadcaster)
    {
        _run = run;
        _logger = logger;
        _broadcaster = broadcaster;
    }
    
    private async Task AddLogAndBroadcastAsync(AgentRunLog log)
    {
        _run.Logs.Add(log);
        await _broadcaster.BroadcastLogAsync(_run.Id, _run.AgentId, log);
    }

    public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        // Log before function invocation
        var arguments = context.Arguments.Select(kvp => new
        {
            name = kvp.Key,
            value = kvp.Value?.ToString() ?? "null",
            type = kvp.Value?.GetType().Name ?? "null"
        }).ToList();

        await AddLogAndBroadcastAsync(new AgentRunLog
        {
            Timestamp = DateTime.UtcNow,
            Level = "info",
            Message = $"Tool call started: {context.Function.PluginName}.{context.Function.Name}",
            Data = new Dictionary<string, object>
            {
                { "function", context.Function.Name },
                { "plugin", context.Function.PluginName ?? "default" },
                { "description", context.Function.Description ?? "" },
                { "arguments", arguments }
            }
        });

        _logger.LogInformation("Tool call: {Plugin}.{Function} with arguments: {Args}",
            context.Function.PluginName,
            context.Function.Name,
            string.Join(", ", arguments.Select(a => $"{a.name}={a.value}")));

        try
        {
            // Execute the function
            await next(context);

            // Log after successful invocation
            var resultObj = context.Result?.GetValue<object>();
            
            // Extract the actual string value if it's wrapped in a JsonElement or similar
            string resultString;
            if (resultObj is System.Text.Json.JsonElement jsonElement)
            {
                resultString = jsonElement.GetRawText();
            }
            else
            {
                resultString = resultObj?.ToString() ?? "no result";
            }
            
            var resultPreview = resultString.Length > 200 ? resultString.Substring(0, 200) + "..." : resultString;

            await AddLogAndBroadcastAsync(new AgentRunLog
            {
                Timestamp = DateTime.UtcNow,
                Level = "info",
                Message = $"Tool call completed: {context.Function.PluginName}.{context.Function.Name}",
                Data = new Dictionary<string, object>
                {
                    { "function", context.Function.Name },
                    { "plugin", context.Function.PluginName ?? "default" },
                    { "success", true },
                    { "result", resultString },
                    { "resultPreview", resultPreview }
                }
            });

            _logger.LogInformation("Tool call completed: {Plugin}.{Function} - Result: {Result}",
                context.Function.PluginName,
                context.Function.Name,
                resultPreview);
        }
        catch (Exception ex)
        {
            // Log error
            await AddLogAndBroadcastAsync(new AgentRunLog
            {
                Timestamp = DateTime.UtcNow,
                Level = "error",
                Message = $"Tool call failed: {context.Function.PluginName}.{context.Function.Name}",
                Data = new Dictionary<string, object>
                {
                    { "function", context.Function.Name },
                    { "plugin", context.Function.PluginName ?? "default" },
                    { "success", false },
                    { "error", ex.Message },
                    { "errorType", ex.GetType().Name }
                }
            });

            _logger.LogError(ex, "Tool call failed: {Plugin}.{Function}",
                context.Function.PluginName,
                context.Function.Name);

            throw;
        }
    }
}
