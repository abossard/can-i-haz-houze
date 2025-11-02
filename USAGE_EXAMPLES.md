# Usage Examples - Frontend Performance Improvements

## Toast Notifications

### Automatic HTTP Error Handling
All HTTP errors are now automatically shown as toast notifications. You don't need to add any code!

```csharp
// This code will automatically show a toast if the API call fails
var documents = await DocumentApi.GetDocumentsAsync(username);

// No try-catch needed! The ErrorHandlingDelegatingHandler handles it.
```

### Manual Toast Notifications
If you want to show custom toast messages in your components:

```csharp
@inject ToastService ToastService

private async Task SaveData()
{
    try
    {
        // Your code here
        await SomeApiCall();
        
        // Show success message
        ToastService.ShowSuccess("Data saved successfully!");
    }
    catch (Exception ex)
    {
        // Manually show error if needed
        ToastService.ShowError($"Failed to save: {ex.Message}");
    }
}
```

### Toast Types
```csharp
// Success (green, 5 seconds)
ToastService.ShowSuccess("Operation completed!");

// Error (red, 7 seconds)
ToastService.ShowError("Something went wrong!");

// Warning (yellow, 6 seconds)
ToastService.ShowWarning("Please check your input");

// Info (blue, 5 seconds)
ToastService.ShowInfo("Did you know...");

// Custom duration
ToastService.ShowToast("Custom message", ToastType.Success, durationSeconds: 10);
```

## Loading Indicators

### Using the LoadingIndicator Component

In your Razor pages:

```razor
@using CanIHazHouze.Web.Components.Shared

@if (isLoading)
{
    <LoadingIndicator IsLoading="true" Message="Loading data..." />
}
else
{
    <!-- Your content here -->
}
```

### LoadingIndicator Options

```razor
<!-- Simple loading spinner -->
<LoadingIndicator IsLoading="@isLoading" />

<!-- With message -->
<LoadingIndicator IsLoading="@isLoading" Message="Please wait..." />

<!-- Full page overlay -->
<LoadingIndicator IsLoading="@isLoading" FullPage="true" Message="Processing..." />

<!-- Custom size -->
<LoadingIndicator IsLoading="@isLoading" Size="5rem" />
```

## Error Boundaries

### Handling Component Errors
The ErrorBoundary in MainLayout catches all rendering errors automatically. To add custom error boundaries:

```razor
<ErrorBoundary>
    <ChildContent>
        <YourComponent />
    </ChildContent>
    <ErrorContent Context="error">
        <div class="alert alert-danger">
            <h4>Oops!</h4>
            <p>This component encountered an error: @error.Message</p>
            <button @onclick="RecoverFromError">Retry</button>
        </div>
    </ErrorContent>
</ErrorBoundary>
```

## HTTP Client Configuration

### Adding Error Handling to New Services

When adding a new HTTP client service:

```csharp
builder.Services.AddHttpClient<YourNewApiClient>(client =>
{
    client.BaseAddress = new("https+http://yournewservice");
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddHttpMessageHandler<ErrorHandlingDelegatingHandler>(); // This line adds error toasts!
```

## Performance Best Practices

### Page Loading Patterns

For pages that load data:

```razor
@page "/mypage"
@rendermode InteractiveServer
@using CanIHazHouze.Web.Components.Shared

@inject MyApiClient ApiClient

<PageTitle>My Page</PageTitle>

<h1>My Page</h1>

@if (isLoading)
{
    <LoadingIndicator IsLoading="true" Message="Loading..." />
}
else if (data == null)
{
    <div class="alert alert-info">
        No data available
    </div>
}
else
{
    <!-- Render your data -->
}

@code {
    private bool isLoading = true;
    private MyData? data;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            data = await ApiClient.GetDataAsync();
        }
        finally
        {
            isLoading = false;
        }
    }
}
```

### Stream Rendering for Better Performance

For pages that can benefit from server-side rendering:

```razor
@page "/dashboard"
@attribute [StreamRendering(true)]
@attribute [OutputCache(Duration = 5)]

<!-- This page will render on the server first, then stream updates -->
```

## CSS Classes for Enhanced UI

### Toast-like Alerts
```html
<!-- Use toast animations for custom alerts -->
<div class="alert alert-info toast show">
    Your message here
</div>
```

### Loading Skeleton
```html
<!-- Show placeholder content while loading -->
<div class="loading-skeleton" style="height: 100px; width: 100%; border-radius: 4px;">
</div>
```

### Smooth Transitions
All buttons and cards automatically have smooth transitions. To add to other elements:

```html
<div style="transition: opacity 0.2s ease-in-out;">
    Content
</div>
```

## Debugging

### Checking Toast Events
Open browser console to see toast-related logs:
```
ToastService: Showing error toast - "Document Service: Request failed"
```

### Checking HTTP Errors
The ErrorHandlingDelegatingHandler logs all HTTP issues:
```
HTTP 404 from Document Service: Resource not found
```

### Verifying Blazor Circuit
Check console for Blazor initialization:
```
CanIHazHouze: Page loaded, initializing Blazor...
```

## Common Patterns

### Refreshing Data with Feedback

```csharp
private async Task RefreshData()
{
    isLoading = true;
    try
    {
        data = await ApiClient.GetDataAsync();
        ToastService.ShowSuccess("Data refreshed!");
    }
    catch
    {
        // Error toast shown automatically by delegating handler
        // Optionally add custom message
        ToastService.ShowWarning("Using cached data");
    }
    finally
    {
        isLoading = false;
    }
}
```

### Form Submission with Validation

```csharp
private async Task HandleSubmit()
{
    if (!ValidateForm())
    {
        ToastService.ShowWarning("Please fill all required fields");
        return;
    }

    isSubmitting = true;
    try
    {
        await ApiClient.SubmitFormAsync(formData);
        ToastService.ShowSuccess("Form submitted successfully!");
        NavigationManager.NavigateTo("/success");
    }
    finally
    {
        isSubmitting = false;
    }
}
```

### Long-Running Operations

```csharp
private async Task ProcessLargeFile()
{
    var progressToast = ToastService.ShowInfo("Processing file...", 30); // 30 seconds
    
    try
    {
        await ApiClient.ProcessFileAsync(file);
        ToastService.ShowSuccess("File processed successfully!");
    }
    catch
    {
        // Error handled automatically
    }
}
```

## Configuration

### Adjusting Toast Duration
You can modify default durations in ToastService.cs:

```csharp
public void ShowError(string message, int durationSeconds = 7)  // Change default here
```

### Adjusting Circuit Options
Modify in Program.cs:

```csharp
builder.Services.AddServerSideBlazor(options =>
{
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(5); // Change retention
    options.JSInteropDefaultCallTimeout = TimeSpan.FromSeconds(30); // Change timeout
});
```

### Adjusting HTTP Timeouts
Modify per client in Program.cs:

```csharp
builder.Services.AddHttpClient<DocumentApiClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60); // Longer timeout for file uploads
})
```

## Testing

### Testing Toast Notifications
```csharp
[Fact]
public void ToastService_ShowsSuccessMessage()
{
    var toastService = new ToastService();
    var messageReceived = false;
    
    toastService.OnShow += (toast) => {
        messageReceived = true;
        Assert.Equal(ToastType.Success, toast.Type);
    };
    
    toastService.ShowSuccess("Test");
    Assert.True(messageReceived);
}
```

### Testing Error Handling
```csharp
[Fact]
public async Task ErrorHandler_ShowsToastOnError()
{
    var toastService = new Mock<ToastService>();
    var handler = new ErrorHandlingDelegatingHandler(toastService.Object, logger);
    
    // Simulate HTTP error
    // Verify toast was shown
}
```

## Troubleshooting

### Toasts Not Appearing
1. Check that `ToastContainer` is in MainLayout.razor
2. Verify `ToastService` is registered in Program.cs as Scoped
3. Check browser console for errors

### Loading Indicators Not Showing
1. Verify `@using CanIHazHouze.Web.Components.Shared` is present
2. Check that `isLoading` flag is being set correctly
3. Ensure component is using `@rendermode InteractiveServer`

### HTTP Errors Not Showing Toasts
1. Verify `ErrorHandlingDelegatingHandler` is registered
2. Check that HTTP client includes `.AddHttpMessageHandler<ErrorHandlingDelegatingHandler>()`
3. Look in browser console for handler logs

## Additional Resources

- See `FRONTEND_PERFORMANCE_IMPROVEMENTS.md` for complete implementation details
- Check the source code in `/src/CanIHazHouze.Web/Services/` for service implementations
- View component examples in `/src/CanIHazHouze.Web/Components/Pages/`
