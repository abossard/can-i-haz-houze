# Blazor Server Performance Optimization Guide

## ✅ Already Implemented

### Program.cs Configuration
- ✅ Response compression (Brotli + Gzip)
- ✅ Output caching
- ✅ Circuit retention limits (100 disconnected circuits, 3-minute retention)
- ✅ Buffered render batch limits (10 max)
- ✅ SignalR hub options (10MB max message size, timeouts configured)
- ✅ HTTP client timeouts configured
- ✅ Scoped services for proper lifecycle management

**Note on Long Polling**: In .NET 9, WebSockets is the default and recommended transport. Long Polling is automatically used as a fallback when WebSockets isn't available. To force Long Polling only (if needed for specific proxy environments), configure it client-side via JavaScript in `wwwroot/` or via the Blazor reconnection UI configuration.

### Fixed Issues
- ✅ **Agents.razor button issue**: Moved `@inject IJSRuntime` to top of file (all injections must be before markup)
- ✅ **Duplicate endpoint error**: Removed duplicate `AddServerSideBlazor()` and `MapBlazorHub()` calls that conflicted with `AddInteractiveServerComponents()`

## 🚀 Next Steps for Extreme Scale (100K+ Users)

### 1. Azure SignalR Service Integration

**Current Capacity**: 5,000-20,000 concurrent users per instance  
**With Azure SignalR**: 100,000+ concurrent users per instance

```bash
# Install package
dotnet add package Microsoft.Azure.SignalR
```

```csharp
// In Program.cs, replace AddServerSideBlazor with:
builder.Services.AddServerSideBlazor()
    .AddAzureSignalR(builder.Configuration["Azure:SignalR:ConnectionString"]);
```

**Azure Portal Setup**:
1. Create Azure SignalR Service (Standard or Premium tier)
2. Enable auto-scale:
   - Scale out when Connection Quota > 70-80%
   - Scale out when Server Load > 80-90%
   - Min 30-minute interval between scale operations

### 2. Component-Level Optimizations

#### Use `@key` for Lists
```razor
@foreach (var agent in agents)
{
    <div @key="agent.Id" class="card">
        <!-- content -->
    </div>
}
```

#### Implement `ShouldRender()` Override
```csharp
protected override bool ShouldRender()
{
    // Only re-render when necessary
    return hasChanges;
}
```

#### Use `<Virtualize>` for Large Lists
```razor
<Virtualize Items="@agents" Context="agent">
    <div class="card">
        <h5>@agent.Name</h5>
    </div>
</Virtualize>
```

### 3. State Management Best Practices

```csharp
// Use ProtectedBrowserStorage for persistent state
@inject ProtectedLocalStorage LocalStorage

private async Task SaveState()
{
    await LocalStorage.SetAsync("agents-filter", currentFilter);
}
```

### 4. Memory Management

#### Implement IDisposable in Components
```csharp
@implements IDisposable

@code {
    private Timer? timer;
    
    public void Dispose()
    {
        timer?.Dispose();
    }
}
```

#### Streaming Large Files
```csharp
// Don't load entire file into memory
public async Task<IActionResult> DownloadLargeDocument(string id)
{
    var stream = await _storage.OpenReadAsync(id);
    return File(stream, "application/pdf");
}
```

### 5. Database Query Optimization

```csharp
// Implement paging for large datasets
public async Task<List<Agent>> GetAgentsAsync(int page = 1, int pageSize = 20)
{
    return await _container
        .GetItemLinqQueryable<Agent>()
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();
}
```

### 6. Monitoring & Diagnostics

Add Application Insights:
```bash
dotnet add package Microsoft.ApplicationInsights.AspNetCore
```

```csharp
// In Program.cs
builder.Services.AddApplicationInsightsTelemetry();
```

Monitor these metrics:
- Circuit count and retention
- Memory usage per user
- SignalR message frequency
- Database query performance
- HTTP client latency

### 7. Production Deployment Checklist

#### Azure Container Apps Configuration
```yaml
# In azure.yaml or deployment template
resources:
  web:
    type: container
    properties:
      scaling:
        minReplicas: 2
        maxReplicas: 10
        rules:
          - http:
              metadata:
                concurrentRequests: "100"
```

#### Load Balancing
- Use Azure Front Door or Application Gateway
- Enable sticky sessions (session affinity) for SignalR
- Configure health probes

#### Caching Strategy
```csharp
// Cache expensive operations
builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache(); // Use Redis in production

// In components:
@inject IMemoryCache Cache

private async Task<List<Agent>> GetCachedAgents()
{
    return await Cache.GetOrCreateAsync("agents", async entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
        return await AgentApi.GetAgentsAsync();
    });
}
```

### 8. Performance Testing

Test with realistic load:
```bash
# Install tools
dotnet tool install -g Microsoft.Crank.Controller

# Run load test
crank --config load-test.yml --scenario blazor-agents --profile production
```

## 📊 Expected Performance Metrics

| Configuration | Concurrent Users | Latency (p95) | Memory/User |
|--------------|------------------|---------------|-------------|
| Single D1_v2 | 5,000 | <200ms | ~700KB |
| Single D3_V2 | 20,000 | <200ms | ~700KB |
| With Azure SignalR | 100,000+ | <200ms | ~500KB |

## 🔍 Troubleshooting

### Buttons Not Working
- **Cause**: `@inject` directives after markup/code
- **Fix**: Move all `@inject` to top of .razor file

### High Memory Usage
- Check for undisposed `IDisposable` resources
- Verify circuit retention limits
- Review component state size

### SignalR Disconnections
- Enable Long Polling (already configured)
- Increase circuit retention period
- Check corporate proxy/firewall settings

### Slow Initial Load
- Implement pre-rendering
- Lazy-load components
- Optimize static asset delivery (use CDN)

## 📚 References

- [Blazor Server Performance Best Practices](https://learn.microsoft.com/aspnet/core/blazor/performance)
- [Azure SignalR Service](https://learn.microsoft.com/azure/azure-signalr/)
- [Blazor Server Scalability](https://devblogs.microsoft.com/dotnet/blazor-server-in-net-core-3-0-scenarios-and-performance/)
