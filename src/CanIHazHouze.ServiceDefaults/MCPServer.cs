using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Model Context Protocol (MCP) server implementation for Aspire services
/// </summary>
public interface IMCPServer
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    void RegisterTool<T>(string name, string description, Func<T, Task<object>> handler);
    void RegisterResource(string uri, string name, string description, Func<Task<object>> provider);
    Task<object> HandleToolCallAsync(string toolName, JsonElement arguments);
    Task<object> HandleResourceRequestAsync(string resourceUri);
    IEnumerable<MCPTool> GetAvailableTools();
    IEnumerable<MCPResource> GetAvailableResources();
}

/// <summary>
/// MCP tool definition
/// </summary>
public record MCPTool(
    string Name,
    string Description,
    JsonDocument InputSchema
);

/// <summary>
/// MCP resource definition  
/// </summary>
public record MCPResource(
    string Uri,
    string Name,
    string Description,
    string MimeType = "application/json"
);

/// <summary>
/// MCP configuration options
/// </summary>
public class MCPOptions
{
    public bool Enabled { get; set; } = true;
    public string Endpoint { get; set; } = "/mcp";
    public int MaxConnections { get; set; } = 100;
    public long MessageSizeLimit { get; set; } = 1024 * 1024; // 1MB
    public string[] AllowedOrigins { get; set; } = ["*"];
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromMinutes(30);
}

/// <summary>
/// Default MCP server implementation
/// </summary>
public class AspireMCPServer : IMCPServer
{
    private readonly ILogger<AspireMCPServer> _logger;
    private readonly MCPOptions _options;
    private readonly Dictionary<string, (string description, Func<JsonElement, Task<object>> handler, JsonDocument schema)> _tools = new();
    private readonly Dictionary<string, (string name, string description, Func<Task<object>> provider)> _resources = new();

    public AspireMCPServer(ILogger<AspireMCPServer> logger, IOptions<MCPOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting MCP Server on endpoint {Endpoint}", _options.Endpoint);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping MCP Server");
        return Task.CompletedTask;
    }

    public void RegisterTool<T>(string name, string description, Func<T, Task<object>> handler)
    {
        var schema = GenerateJsonSchema<T>();
        
        _tools[name] = (description, async args =>
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                var typedArgs = JsonSerializer.Deserialize<T>(args.GetRawText(), options);
                if (typedArgs == null)
                    throw new ArgumentException($"Invalid arguments for tool {name}");
                
                return await handler(typedArgs);
            }
            catch (JsonException ex)
            {
                throw new ArgumentException($"Failed to deserialize arguments for tool {name}: {ex.Message}", ex);
            }
        }, schema);

        _logger.LogDebug("Registered MCP tool: {ToolName}", name);
    }

    public void RegisterResource(string uri, string name, string description, Func<Task<object>> provider)
    {
        _resources[uri] = (name, description, provider);
        _logger.LogDebug("Registered MCP resource: {ResourceUri}", uri);
    }

    public async Task<object> HandleToolCallAsync(string toolName, JsonElement arguments)
    {
        if (!_tools.TryGetValue(toolName, out var tool))
        {
            throw new InvalidOperationException($"Tool '{toolName}' not found");
        }

        _logger.LogDebug("Executing MCP tool: {ToolName}", toolName);
        
        try
        {
            return await tool.handler(arguments);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing MCP tool {ToolName}", toolName);
            throw;
        }
    }

    public async Task<object> HandleResourceRequestAsync(string resourceUri)
    {
        if (!_resources.TryGetValue(resourceUri, out var resource))
        {
            throw new InvalidOperationException($"Resource '{resourceUri}' not found");
        }

        _logger.LogDebug("Accessing MCP resource: {ResourceUri}", resourceUri);
        
        try
        {
            return await resource.provider();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error accessing MCP resource {ResourceUri}", resourceUri);
            throw;
        }
    }

    public IEnumerable<MCPTool> GetAvailableTools()
    {
        return _tools.Select(kvp => new MCPTool(
            kvp.Key,
            kvp.Value.description,
            kvp.Value.schema
        ));
    }

    public IEnumerable<MCPResource> GetAvailableResources()
    {
        return _resources.Select(kvp => new MCPResource(
            kvp.Key,
            kvp.Value.name,
            kvp.Value.description
        ));
    }

    private static JsonDocument GenerateJsonSchema<T>()
    {
        // Simple schema generation - in production you might want to use a proper JSON schema library
        var type = typeof(T);
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var prop in type.GetProperties())
        {
            var propName = JsonNamingPolicy.CamelCase.ConvertName(prop.Name);
            var propType = GetJsonSchemaType(prop.PropertyType);
            
            properties[propName] = new { type = propType };
            
            // Mark non-nullable reference types and value types as required
            if (!IsNullable(prop.PropertyType))
            {
                required.Add(propName);
            }
        }

        var schema = new
        {
            type = "object",
            properties = properties,
            required = required.ToArray()
        };

        var json = JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true });
        return JsonDocument.Parse(json);
    }

    private static string GetJsonSchemaType(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        
        return Type.GetTypeCode(underlyingType) switch
        {
            TypeCode.Boolean => "boolean",
            TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or 
            TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 => "integer",
            TypeCode.Single or TypeCode.Double or TypeCode.Decimal => "number",
            TypeCode.DateTime => "string",
            TypeCode.String => "string",
            _ when underlyingType == typeof(Guid) => "string",
            _ when underlyingType.IsEnum => "string",
            _ when underlyingType.IsArray => "array",
            _ => "object"
        };
    }

    private static bool IsNullable(Type type)
    {
        return !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
    }
}