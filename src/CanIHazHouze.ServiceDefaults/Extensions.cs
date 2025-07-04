using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Microsoft.Extensions.Hosting;

// Adds common .NET Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/dotnet/aspire/service-defaults
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default
            http.AddStandardResilienceHandler();

            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        // Add MCP support by default
        builder.AddMCPSupport();

        // Uncomment the following to restrict the allowed schemes for service discovery.
        // builder.Services.Configure<ServiceDiscoveryOptions>(options =>
        // {
        //     options.AllowedSchemes = ["https"];
        // });

        return builder;
    }

    /// <summary>
    /// Adds MCP (Model Context Protocol) server support to the application
    /// </summary>
    public static TBuilder AddMCPSupport<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        // Configure MCP options from configuration
        builder.Services.Configure<MCPOptions>(
            builder.Configuration.GetSection("MCP"));

        // Register MCP server as singleton
        builder.Services.AddSingleton<IMCPServer, AspireMCPServer>();

        // Add hosted service to start/stop MCP server
        builder.Services.AddHostedService<MCPServerHostedService>();

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(tracing =>
                        // Exclude health check requests from tracing
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                    )
                    // Uncomment the following line to enable gRPC instrumentation (requires the OpenTelemetry.Instrumentation.GrpcNetClient package)
                    //.AddGrpcClientInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        // Uncomment the following lines to enable the Azure Monitor exporter (requires the Azure.Monitor.OpenTelemetry.AspNetCore package)
        //if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        //{
        //    builder.Services.AddOpenTelemetry()
        //       .UseAzureMonitor();
        //}

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Adding health checks endpoints to applications in non-development environments has security implications.
        // See https://aka.ms/dotnet/aspire/healthchecks for details before enabling these endpoints in non-development environments.
        if (app.Environment.IsDevelopment())
        {
            // All health checks must pass for app to be considered ready to accept traffic after starting
            app.MapHealthChecks(HealthEndpointPath);

            // Only health checks tagged with the "live" tag must pass for app to be considered alive
            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
        }

        // Map MCP endpoints
        app.MapMCPEndpoints();

        return app;
    }

    /// <summary>
    /// Maps MCP (Model Context Protocol) endpoints
    /// </summary>
    public static WebApplication MapMCPEndpoints(this WebApplication app)
    {
        var mcpOptions = app.Services.GetService<IOptions<MCPOptions>>();
        if (mcpOptions?.Value?.Enabled != true)
        {
            return app;
        }

        var mcpServer = app.Services.GetRequiredService<IMCPServer>();
        var logger = app.Services.GetRequiredService<ILogger<AspireMCPServer>>();

        // Map WebSocket endpoint for MCP
        app.Map(mcpOptions.Value.Endpoint, async context =>
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                await HandleMCPWebSocketConnection(webSocket, mcpServer, logger);
            }
            else
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("WebSocket connection required for MCP endpoint");
            }
        });

        // Add HTTP endpoint for MCP capabilities discovery
        app.MapGet($"{mcpOptions.Value.Endpoint}/capabilities", (IMCPServer server) =>
        {
            return Results.Ok(new
            {
                capabilities = new
                {
                    tools = server.GetAvailableTools().Select(t => new
                    {
                        name = t.Name,
                        description = t.Description,
                        inputSchema = JsonSerializer.Deserialize<object>(t.InputSchema.RootElement.GetRawText())
                    }),
                    resources = server.GetAvailableResources().Select(r => new
                    {
                        uri = r.Uri,
                        name = r.Name,
                        description = r.Description,
                        mimeType = r.MimeType
                    })
                },
                protocolVersion = "2024-11-05",
                serverInfo = new
                {
                    name = "CanIHazHouze MCP Server",
                    version = "1.0.0"
                }
            });
        })
        .WithName("MCPCapabilities")
        .WithSummary("Get MCP server capabilities")
        .WithDescription("Returns available tools and resources for Model Context Protocol clients")
        .WithTags("MCP");

        logger.LogInformation("MCP endpoints mapped at {Endpoint}", mcpOptions.Value.Endpoint);
        return app;
    }

    private static async Task HandleMCPWebSocketConnection(WebSocket webSocket, IMCPServer mcpServer, ILogger logger)
    {
        var buffer = new byte[1024 * 4];
        
        try
        {
            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    logger.LogDebug("Received MCP message: {Message}", message);

                    try
                    {
                        var response = await ProcessMCPMessage(message, mcpServer);
                        var responseBytes = Encoding.UTF8.GetBytes(response);
                        
                        await webSocket.SendAsync(
                            new ArraySegment<byte>(responseBytes), 
                            WebSocketMessageType.Text, 
                            true, 
                            CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error processing MCP message");
                        
                        var errorResponse = JsonSerializer.Serialize(new
                        {
                            jsonrpc = "2.0",
                            error = new
                            {
                                code = -1,
                                message = ex.Message
                            },
                            id = (object?)null
                        });
                        
                        var errorBytes = Encoding.UTF8.GetBytes(errorResponse);
                        await webSocket.SendAsync(
                            new ArraySegment<byte>(errorBytes), 
                            WebSocketMessageType.Text, 
                            true, 
                            CancellationToken.None);
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WebSocket connection error");
        }
        finally
        {
            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", CancellationToken.None);
            }
        }
    }

    private static async Task<string> ProcessMCPMessage(string message, IMCPServer mcpServer)
    {
        using var document = JsonDocument.Parse(message);
        var root = document.RootElement;
        
        var method = root.GetProperty("method").GetString();
        var id = root.TryGetProperty("id", out var idProp) ? idProp : (JsonElement?)null;

        object result = method switch
        {
            "initialize" => new
            {
                protocolVersion = "2024-11-05",
                capabilities = new
                {
                    tools = new { },
                    resources = new { }
                },
                serverInfo = new
                {
                    name = "CanIHazHouze MCP Server",
                    version = "1.0.0"
                }
            },
            "tools/list" => new
            {
                tools = mcpServer.GetAvailableTools().Select(t => new
                {
                    name = t.Name,
                    description = t.Description,
                    inputSchema = JsonSerializer.Deserialize<object>(t.InputSchema.RootElement.GetRawText())
                })
            },
            "tools/call" => await HandleToolCall(root, mcpServer),
            "resources/list" => new
            {
                resources = mcpServer.GetAvailableResources().Select(r => new
                {
                    uri = r.Uri,
                    name = r.Name,
                    description = r.Description,
                    mimeType = r.MimeType
                })
            },
            "resources/read" => await HandleResourceRead(root, mcpServer),
            _ => throw new InvalidOperationException($"Unknown method: {method}")
        };

        var response = new
        {
            jsonrpc = "2.0",
            result = result,
            id = id?.ValueKind == JsonValueKind.Null ? (object?)null : 
                id?.ValueKind == JsonValueKind.String ? id?.GetString() : 
                id?.ValueKind == JsonValueKind.Number ? id?.GetInt32() : 
                (object?)null
        };

        return JsonSerializer.Serialize(response);
    }

    private static async Task<object> HandleToolCall(JsonElement root, IMCPServer mcpServer)
    {
        var paramsElement = root.GetProperty("params");
        var toolName = paramsElement.GetProperty("name").GetString() ?? "";
        var arguments = paramsElement.GetProperty("arguments");

        var result = await mcpServer.HandleToolCallAsync(toolName, arguments);
        
        return new
        {
            content = new[]
            {
                new
                {
                    type = "text",
                    text = JsonSerializer.Serialize(result)
                }
            }
        };
    }

    private static async Task<object> HandleResourceRead(JsonElement root, IMCPServer mcpServer)
    {
        var paramsElement = root.GetProperty("params");
        var uri = paramsElement.GetProperty("uri").GetString() ?? "";

        var result = await mcpServer.HandleResourceRequestAsync(uri);
        
        return new
        {
            contents = new[]
            {
                new
                {
                    uri = uri,
                    mimeType = "application/json",
                    text = JsonSerializer.Serialize(result)
                }
            }
        };
    }

    /// <summary>
    /// Adds OpenAPI configuration with Azure Container Apps server URL detection
    /// </summary>
    public static TBuilder AddOpenApiWithAzureContainerAppsServers<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, context, cancellationToken) =>
            {
                // Clear any existing servers
                document.Servers.Clear();

                // Try to construct server URL from Azure Container Apps environment variables
                var configuration = context.ApplicationServices.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
                var containerAppName = configuration["CONTAINER_APP_NAME"];
                var containerAppEnvDnsSuffix = configuration["CONTAINER_APP_ENV_DNS_SUFFIX"];

                if (!string.IsNullOrEmpty(containerAppName) && !string.IsNullOrEmpty(containerAppEnvDnsSuffix))
                {
                    var serverUrl = $"https://{containerAppName}.{containerAppEnvDnsSuffix}";

                    document.Servers.Add(new OpenApiServer
                    {
                        Url = serverUrl,
                        Description = "Azure Container Apps server"
                    });                    // Log the constructed URL for debugging
                    var loggerFactory = context.ApplicationServices.GetService<ILoggerFactory>();
                    var logger = loggerFactory?.CreateLogger("OpenApiServerConfiguration");
                    logger?.LogInformation("OpenAPI Server URL constructed from environment variables: {ServerUrl}", serverUrl);
                }
                else
                {
                    // Log missing environment variables for debugging
                    var loggerFactory = context.ApplicationServices.GetService<ILoggerFactory>();
                    var logger = loggerFactory?.CreateLogger("OpenApiServerConfiguration");
                    logger?.LogDebug("Azure Container Apps environment variables not found. CONTAINER_APP_NAME: {AppName}, CONTAINER_APP_ENV_DNS_SUFFIX: {DnsSuffix}",
                        containerAppName ?? "null", containerAppEnvDnsSuffix ?? "null");
                }

                // Remove health check endpoints from OpenAPI spec
                var pathsToRemove = document.Paths.Keys
                    .Where(path => path.StartsWith(HealthEndpointPath) || path.StartsWith(AlivenessEndpointPath))
                    .ToList();

                foreach (var path in pathsToRemove)
                {
                    document.Paths.Remove(path);
                }

                return Task.CompletedTask;
            });
        });

        return builder;
    }
}
