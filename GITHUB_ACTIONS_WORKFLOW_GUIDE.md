# GitHub Actions Workflow for Aspire Dashboard

## Overview

This document explains the `.github/workflows/aspire-dashboard-test.yml` workflow that successfully runs the .NET Aspire AppHost in GitHub Actions and captures screenshots.

## The Problem

.NET Aspire's DCP (Developer Control Plane) was failing to initialize in GitHub Actions with this error:

```
System.Net.Sockets.SocketException (61): No data available
Connection refused: [::1]:39643
```

**Root Cause:** DCP was trying to bind its API server to IPv6 localhost (`[::1]`), but the GitHub Actions environment couldn't establish the connection properly, causing a 20-second timeout.

## The Solution

### Key Configuration

The fix is simple but crucial - disable IPv6 for .NET:

```yaml
env:
  DOTNET_SYSTEM_NET_DISABLEIPV6: '1'
```

This forces .NET to use IPv4 (`127.0.0.1`) instead of IPv6 (`[::1]`) for localhost binding, which resolves the DCP initialization issue.

### Workflow Features

1. **Pre-pull Docker Images**
   - Cosmos DB Linux Emulator
   - Azurite Storage Emulator
   - Runs in parallel for faster setup

2. **Aspire Workload Installation**
   - Installs the Aspire workload on the runner
   - Required for proper AppHost execution

3. **Smart Dashboard Detection**
   - Tries multiple common ports: 15097, 17032, 17001, 18888
   - Waits up to 5 minutes for dashboard to be ready
   - Monitors AppHost process health

4. **Screenshot Capture**
   - Uses headless Chromium for screenshots
   - Captures both dashboard and web frontend
   - Runs in virtual display (Xvfb)

5. **Comprehensive Logging**
   - Full AppHost log included in artifacts
   - Step-by-step progress indicators
   - Diagnostic information on failure

## How to Use

### Manual Trigger (GitHub UI)

1. Navigate to your repository on GitHub
2. Click the "Actions" tab
3. Select "Aspire Dashboard Test" from the workflows list
4. Click "Run workflow" button (top right)
5. Select branch (usually the PR branch)
6. Click the green "Run workflow" button
7. Wait for workflow to complete (~5-10 minutes)
8. Click on the completed run
9. Scroll down to "Artifacts" section
10. Download "aspire-screenshots" artifact
11. Extract ZIP to view:
    - `dashboard-screenshot.png` - Aspire Dashboard
    - `webfrontend-screenshot.png` - Web Frontend
    - `apphost.log` - Full logs

### Command Line (gh CLI)

```bash
# Trigger workflow
gh workflow run aspire-dashboard-test.yml

# View recent runs
gh run list --workflow=aspire-dashboard-test.yml

# Download artifacts from latest run
gh run download $(gh run list --workflow=aspire-dashboard-test.yml --limit 1 --json databaseId -q '.[0].databaseId')
```

### Automatic Trigger

The workflow also runs automatically on:
- Pull requests to `main` or `develop` branches
- When changes affect `src/**` or the workflow file itself

## Workflow Architecture

```
┌─────────────────────────────────────────────────────────────┐
│ 1. Setup Environment                                        │
│    - .NET 9.0 SDK                                          │
│    - Docker (pre-installed)                                │
│    - Set DOTNET_SYSTEM_NET_DISABLEIPV6=1                  │
└─────────────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────────────┐
│ 2. Prepare Dependencies                                     │
│    - Pull Cosmos DB & Azurite images                       │
│    - Install Aspire workload                               │
│    - Restore & build solution                              │
└─────────────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────────────┐
│ 3. Start AppHost                                            │
│    - Launch in background with nohup                       │
│    - Capture PID for monitoring                            │
│    - Log to apphost.log                                    │
└─────────────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────────────┐
│ 4. Wait for Ready                                           │
│    - Poll multiple dashboard URLs                          │
│    - Check AppHost process health                          │
│    - Timeout after 5 minutes                               │
└─────────────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────────────┐
│ 5. Capture Screenshots                                      │
│    - Start Xvfb virtual display                            │
│    - Dashboard via headless Chromium                       │
│    - Find web frontend URL from logs                       │
│    - Web frontend screenshot                               │
└─────────────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────────────┐
│ 6. Upload Artifacts                                         │
│    - dashboard-screenshot.png                              │
│    - webfrontend-screenshot.png                            │
│    - apphost.log                                           │
└─────────────────────────────────────────────────────────────┘
```

## Technical Details

### Environment Variables

- `DOTNET_SYSTEM_NET_DISABLEIPV6=1`: Forces IPv4 for .NET networking
- `APPHOST_PID`: Process ID for monitoring
- `DASHBOARD_URL`: Detected dashboard URL
- `WEB_URL`: Detected web frontend URL
- `DISPLAY=:99`: Virtual display for Chromium

### Dependencies Installed

- **chromium-browser**: For screenshot capture
- **xvfb**: Virtual framebuffer for headless display
- **aspire workload**: .NET Aspire orchestration tools

### Timeout Configuration

- Dashboard ready check: 5 minutes (60 attempts × 5 seconds)
- Screenshot operations: Default Chromium timeout (~30 seconds)
- Overall workflow timeout: GitHub Actions default (6 hours)

### Port Scanning Strategy

The workflow checks these ports in order:
1. `http://localhost:15097` - Common Aspire dashboard HTTP port
2. `https://localhost:17032` - Common Aspire dashboard HTTPS port
3. `http://localhost:17001` - Alternative dashboard port
4. `https://localhost:18888` - Fallback port

### Log Parsing

Web frontend URL is extracted from AppHost logs using:
```bash
grep -oP 'webfrontend.*?https?://[^\s]+' src/apphost.log
```

## Troubleshooting

### Workflow Fails: Dashboard Not Ready

**Symptoms:** "Timeout waiting for dashboard" message

**Diagnosis:**
1. Download the `apphost.log` artifact
2. Check for errors in the log
3. Look for port binding messages
4. Verify DCP initialization succeeded

**Common Causes:**
- AppHost crashed during startup
- Incorrect port configuration
- Container pull failures
- Resource exhaustion

### Screenshot Is Black/Empty

**Symptoms:** Screenshot file exists but shows nothing

**Possible Issues:**
- Service not fully rendered when screenshot taken
- JavaScript errors preventing page load
- Authentication/redirect issues

**Solutions:**
- Increase wait time before screenshot
- Add JavaScript wait conditions
- Check browser console logs

### Web Frontend URL Not Found

**Symptoms:** Only dashboard screenshot captured

**Diagnosis:**
- Check `apphost.log` for web frontend startup
- Verify web frontend service configuration
- Look for port assignment messages

## Performance Considerations

### Typical Execution Times

- Setup (checkout, .NET, Docker): ~30 seconds
- Image pulls: ~2-3 minutes
- Aspire workload install: ~30 seconds
- Build: ~1-2 minutes
- AppHost startup: ~1-2 minutes
- Screenshot tools install: ~30 seconds
- Screenshot capture: ~10 seconds
- **Total: ~5-10 minutes**

### Optimization Tips

1. **Cache Dependencies**
   - Use `actions/cache` for Docker images
   - Cache NuGet packages
   - Cache Aspire workload installation

2. **Parallel Operations**
   - Images already pulled in parallel
   - Could parallelize screenshot capture

3. **Smaller Images**
   - Consider lightweight alternatives
   - Use specific image tags vs. `latest`

## Security Considerations

### Secrets Management

Currently no secrets required. If adding Azure OpenAI:

```yaml
env:
  OPENAI_ENDPOINT: ${{ secrets.OPENAI_ENDPOINT }}
  OPENAI_API_KEY: ${{ secrets.OPENAI_API_KEY }}
```

### Network Security

- Workflow runs in isolated environment
- Containers use default Docker networks
- No external network access required
- Screenshots don't expose sensitive data

### Artifact Security

- Artifacts are private to repository
- Only users with repository access can download
- Artifacts expire after 90 days (default)
- Can configure retention period

## Extending the Workflow

### Add API Testing

```yaml
- name: Test APIs
  run: |
    # Wait for services
    sleep 30
    # Test endpoints
    curl http://localhost:5000/health
    curl http://localhost:5001/api/mortgages
```

### Add Performance Metrics

```yaml
- name: Measure Startup Time
  run: |
    echo "AppHost started at: $(date -r src/apphost.log +%s)"
    echo "Dashboard ready at: $(date +%s)"
```

### Add Notification

```yaml
- name: Notify on Success
  if: success()
  uses: actions/github-script@v7
  with:
    script: |
      github.rest.issues.createComment({
        issue_number: context.issue.number,
        owner: context.repo.owner,
        repo: context.repo.repo,
        body: '✅ Dashboard screenshots are ready! Download artifacts to view.'
      })
```

## Comparison with Alternatives

### vs. Local Development

| Aspect | Local | GitHub Actions |
|--------|-------|----------------|
| Setup | Manual | Automated |
| Consistency | Varies | Same every time |
| Sharing | Manual | Artifact download |
| CI Integration | No | Native |
| Cost | Free | Free (public repos) |

### vs. Docker Compose

| Aspect | This Workflow | Docker Compose |
|--------|---------------|----------------|
| Orchestration | Aspire DCP | Docker Compose |
| Dashboard | Yes | No |
| Service Discovery | Automatic | Manual |
| Health Checks | Built-in | Manual |
| Logs | Unified | Separate |

### vs. Azure Container Apps

| Aspect | GitHub Actions | Azure |
|--------|----------------|-------|
| Environment | CI/CD | Production |
| Cost | Free | Paid |
| Persistence | None | Yes |
| Scalability | Limited | High |
| Purpose | Testing | Deployment |

## References

- [.NET Aspire Documentation](https://learn.microsoft.com/en-us/dotnet/aspire/)
- [GitHub Actions Documentation](https://docs.github.com/en/actions)
- [DCP GitHub Issues](https://github.com/dotnet/aspire/issues)
- [Aspire Networking Discussions](https://github.com/dotnet/docs-aspire/issues/232)

## Changelog

### 2025-11-10 - Initial Release

- Created workflow with IPv6 fix
- Added screenshot capture
- Included comprehensive logging
- Documented usage and troubleshooting

## Contributing

To improve this workflow:

1. Test changes locally first
2. Update this documentation
3. Submit PR with clear description
4. Include example run logs/screenshots

## License

This workflow configuration is part of the Can I Haz Houze project and uses the same license as the repository.
