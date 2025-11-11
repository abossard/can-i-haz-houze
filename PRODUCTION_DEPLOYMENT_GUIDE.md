# Production Deployment Guide for Can I Haz Houze

This guide outlines the production deployment configuration and best practices for the Can I Haz Houze Blazor Server application.

## Production Robustness Features

### 🌐 No WebSockets Required - Maximum Corporate Proxy Compatibility

The application is configured to work **WITHOUT WebSockets** for maximum compatibility:

- **Long Polling Only**: The application uses only HTTP Long Polling transport (no WebSocket dependency)
- **Works Through Any Proxy**: Long Polling works through restrictive corporate proxies, firewalls, and VPNs
- **No Special Network Configuration**: Standard HTTP/HTTPS traffic only - no WebSocket upgrade required
- **Automatic Reconnection**: Exponential backoff strategy (immediate, 2s, 5s, 10s, 30s intervals)
- **Extended Timeouts**: Configured for slower corporate network connections

**Why No WebSockets?**
- WebSockets are frequently blocked by corporate proxies and firewalls
- Long Polling uses standard HTTP requests that work everywhere
- Simpler infrastructure - no special load balancer configuration needed for WebSocket upgrades
- More predictable behavior in restrictive network environments

### ⚡ Performance & Scalability (100+ Concurrent Users)

The application is optimized to handle 100+ concurrent users:

- **Response Compression**: Brotli (fastest) and Gzip compression enabled for all responses
- **Circuit Management**: 
  - Max 100 disconnected circuits retained
  - 3-minute retention period for reconnection
  - 10 buffered render batches per circuit
- **Connection Limits**: Kestrel configured for 200 concurrent connections
- **Resource Optimization**: Proper memory management for circuit state

### 🔄 Connection Resilience

- **Automatic Reconnection**: SignalR reconnects automatically with exponential backoff
- **Graceful Degradation**: Application remains functional during temporary network issues
- **Keep-Alive**: 2-minute keep-alive timeout to maintain healthy connections

### 📊 Monitoring & Observability

- **Health Checks**: Built-in health endpoints via .NET Aspire Service Defaults
- **Logging**: SignalR and connection logging enabled in development, warning-level in production
- **Telemetry**: Connection state changes logged for debugging

## Deployment Requirements

### Infrastructure

#### Load Balancer Configuration (Critical for Production)

⚠️ **Session Affinity Required**: Blazor Server maintains stateful connections on the server.

When deploying behind a load balancer or using multiple instances:

1. **Enable Sticky Sessions** (Session Affinity)
   - Each user must be routed to the same server instance throughout their session
   - Configure based on cookies or IP hash
   
2. **Azure Container Apps**:
   ```bash
   # Session affinity is enabled by default
   # Verify in Azure Portal under Container App > Ingress settings
   ```

3. **Kubernetes/Other Load Balancers**:
   ```yaml
   # Example for NGINX Ingress
   nginx.ingress.kubernetes.io/affinity: "cookie"
   nginx.ingress.kubernetes.io/session-cookie-name: "blazor-affinity"
   nginx.ingress.kubernetes.io/session-cookie-hash: "sha1"
   ```

### Resource Requirements (100 Concurrent Users)

- **Memory**: Minimum 2GB RAM (budget 250-300KB per circuit + overhead)
- **CPU**: 2+ cores recommended for production workload
- **Network**: Reliable network connection, low latency preferred

### Environment Variables

Production deployments should configure:

```bash
# Required Azure services
ConnectionStrings__openai=<Azure OpenAI connection string>
ConnectionStrings__cosmos=<Cosmos DB connection string>
ConnectionStrings__storage=<Blob Storage connection string>

# Environment setting (automatically configured via AppHost)
ASPNETCORE_ENVIRONMENT=Production
```

### Automatic Production Configuration

The AppHost is configured to automatically set `ASPNETCORE_ENVIRONMENT=Production` for all services when deploying to Azure Container Apps via `azd`:

**AppHost.cs**:
```csharp
// Configure Production environment for Azure Container Apps deployment
if (builder.ExecutionContext.IsPublishMode)
{
    webFrontend.WithEnvironment("ASPNETCORE_ENVIRONMENT", "Production");
    documentService.WithEnvironment("ASPNETCORE_ENVIRONMENT", "Production");
    ledgerService.WithEnvironment("ASPNETCORE_ENVIRONMENT", "Production");
    mortgageService.WithEnvironment("ASPNETCORE_ENVIRONMENT", "Production");
    crmService.WithEnvironment("ASPNETCORE_ENVIRONMENT", "Production");
    agentService.WithEnvironment("ASPNETCORE_ENVIRONMENT", "Production");
}
```

This ensures:
- All services use their `appsettings.Production.json` files
- Production logging levels are applied (Warning instead of Information)
- Production optimizations are enabled
- Circuit options and Kestrel limits are properly configured

**No manual configuration needed** - When you run `azd up` or `azd deploy`, the Production environment is automatically set.

### Verifying Production Configuration

After deployment, verify the environment is set correctly:

```bash
# Check environment variables in Azure Container App
az containerapp show --name webfrontend --resource-group <resource-group> \
  --query "properties.template.containers[0].env" -o table

# You should see ASPNETCORE_ENVIRONMENT set to "Production"
```

# Optional: Kestrel configuration
ASPNETCORE_URLS=http://+:8080
ASPNETCORE_ENVIRONMENT=Production
```

## SignalR Configuration

### Transport Method: Long Polling Only

The application is configured to use **Long Polling exclusively** - no WebSockets required:

**Server-Side Configuration** (Program.cs):
```csharp
app.MapBlazorHub(options =>
{
    options.Transports = HttpTransportType.LongPolling;
});
```

**Client-Side Configuration** (App.razor):
```javascript
Blazor.start({
    circuit: {
        configureSignalR: function (builder) {
            builder.withUrl("/_blazor", {
                transport: 4, // LongPolling only
                skipNegotiation: false
            });
            builder.withAutomaticReconnect([0, 2000, 5000, 10000, 30000]);
        }
    }
});
```

### How Long Polling Works

Long Polling is a HTTP-based communication pattern:

1. Client sends HTTP request to server
2. Server holds the request open until it has data to send
3. Server sends response with data
4. Client immediately opens a new request
5. Process repeats continuously

**Advantages:**
- Works through ANY proxy or firewall (uses standard HTTP)
- No special infrastructure requirements
- Reliable in restrictive corporate networks
- Compatible with all load balancers and CDNs

**Trade-offs:**
- Slightly higher latency compared to WebSockets (~100-200ms vs ~10ms)
- More HTTP requests (but kept-alive connections minimize overhead)
- Still excellent for interactive applications with 100+ users

### Reconnection Strategy

If a connection drops, SignalR attempts reconnection with exponential backoff:

- **Immediate**: First retry happens immediately
- **2 seconds**: Second retry after 2 seconds
- **5 seconds**: Third retry after 5 seconds
- **10 seconds**: Fourth retry after 10 seconds
- **30 seconds**: Subsequent retries every 30 seconds

### Monitoring Connection Issues

Check logs for SignalR connection information:

```bash
# Look for connection transport information
Microsoft.AspNetCore.Http.Connections: Information
Microsoft.AspNetCore.SignalR: Information

# Expected log messages:
# - "Transport 'LongPolling' started" (confirms Long Polling is being used)
# - "Connection disconnected"
# - "Reconnect attempt X"

# You should NOT see "WebSockets" mentioned in the logs
```

## Testing in Production-Like Environments

### Corporate Proxy Testing

The application is designed to work through ANY corporate proxy:

1. **No Special Configuration**: Long Polling uses standard HTTP - it just works
2. **Test Behind VPN**: Application should work seamlessly through corporate VPNs
3. **Test Reconnection**: Simulate network interruptions to verify auto-reconnect
4. **Verify No WebSocket Attempts**: Check browser dev tools network tab - should only see HTTP requests to `/_blazor`

### Load Testing for 100+ Users

```bash
# Example using Apache Bench (for HTTP endpoints)
ab -n 1000 -c 100 https://your-app-url/

# For SignalR/WebSocket load testing, use specialized tools:
# - SignalR Performance Testing: https://github.com/aspnet/SignalR/tree/main/test/Microsoft.AspNetCore.SignalR.Tests.Utils
# - Artillery.io with WebSocket support
```

Expected results for 100 concurrent users:
- Response time: < 300ms for interactive components (Long Polling adds ~100-200ms overhead)
- Memory usage: ~30-50MB for circuits
- CPU usage: < 50% on 2-core system

**Note**: Long Polling is slightly less performant than WebSockets but still excellent for 100 concurrent users. The reliability benefits in corporate environments far outweigh the minor latency increase.

## Security Considerations

### Production Checklist

- ✅ **HTTPS Only**: Enforce HTTPS in production (configured via `UseHttpsRedirection()`)
- ✅ **HSTS Enabled**: HTTP Strict Transport Security configured
- ✅ **Detailed Errors Disabled**: Circuit detailed errors disabled in production
- ✅ **Response Compression**: Enabled for HTTPS (safe for this application)
- ✅ **Antiforgery Protection**: Enabled for all interactive components

### Additional Security

For additional security in production:

1. **Add Authentication**: Implement ASP.NET Core Identity or external auth providers
2. **API Authorization**: Secure backend service APIs with JWT tokens
3. **Rate Limiting**: Consider adding rate limiting middleware
4. **CORS Configuration**: Restrict CORS to known origins

## Troubleshooting

### Connection Issues

**Symptom**: "Failed to start the connection" errors

**Solutions**:
1. Check that the application is using Long Polling (no WebSocket attempts)
2. Verify HTTP/HTTPS connectivity to the server
3. Check SignalR logs to confirm Long Polling transport is active
4. Ensure firewall allows standard HTTP traffic

### High Memory Usage

**Symptom**: Memory grows beyond expected levels with concurrent users

**Solutions**:
1. Review `DisconnectedCircuitMaxRetained` setting
2. Reduce `DisconnectedCircuitRetentionPeriod` if needed
3. Monitor for memory leaks in application code
4. Consider horizontal scaling with additional instances

### Connection Drops

**Symptom**: Users frequently disconnected or see "Reconnecting..." messages

**Solutions**:
1. Check network stability between client and server
2. Verify load balancer session affinity is configured correctly
3. Increase `KeepAliveTimeout` setting if needed
4. Review firewall/proxy idle timeout settings

### Performance Degradation

**Symptom**: Slow response times with multiple users

**Solutions**:
1. Enable response compression (already configured)
2. Verify static assets are cached properly
3. Scale horizontally by adding more instances
4. Consider using Azure SignalR Service for connection management

## Monitoring Recommendations

### Key Metrics to Track

1. **Active Connections**: Number of concurrent SignalR connections
2. **Circuit Count**: Active and disconnected circuits
3. **Memory Usage**: Per-process memory consumption
4. **Response Times**: P95/P99 latency for interactive operations
5. **Error Rates**: Failed connections, timeouts, exceptions

### Application Insights Integration

The application uses .NET Aspire Service Defaults which include telemetry:

```csharp
// Already configured in ServiceDefaults
builder.AddServiceDefaults(); // Includes telemetry configuration
```

Monitor these custom events:
- SignalR connection established
- SignalR connection disconnected
- Transport type selected (WebSocket vs Long Polling)
- Reconnection attempts

## Additional Resources

- [ASP.NET Core Blazor SignalR Guidance](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/signalr?view=aspnetcore-9.0)
- [Host and Deploy Blazor Server](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/server?view=aspnetcore-9.0)
- [WebSockets at Scale - Best Practices](https://websocket.org/guides/websockets-at-scale/)
- [.NET Aspire Documentation](https://learn.microsoft.com/en-us/dotnet/aspire/)

---

**Last Updated**: 2025-11-11  
**Application Version**: .NET 9.0 with .NET Aspire 9.3.1
