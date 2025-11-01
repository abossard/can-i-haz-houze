using System.Net;

namespace CanIHazHouze.Web.Services;

public class ErrorHandlingDelegatingHandler : DelegatingHandler
{
    private readonly ToastService _toastService;
    private readonly ILogger<ErrorHandlingDelegatingHandler> _logger;

    public ErrorHandlingDelegatingHandler(ToastService toastService, ILogger<ErrorHandlingDelegatingHandler> logger)
    {
        _toastService = toastService;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await base.SendAsync(request, cancellationToken);

            // If the response is not successful, show a toast
            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = await GetErrorMessageAsync(response);
                var serviceName = GetServiceName(request.RequestUri);
                
                _logger.LogWarning("HTTP {StatusCode} from {Service}: {Error}", 
                    response.StatusCode, serviceName, errorMessage);

                // Show user-friendly error toast
                var userMessage = response.StatusCode switch
                {
                    HttpStatusCode.NotFound => $"{serviceName}: Resource not found",
                    HttpStatusCode.Unauthorized => $"{serviceName}: Authentication required",
                    HttpStatusCode.Forbidden => $"{serviceName}: Access denied",
                    HttpStatusCode.BadRequest => $"{serviceName}: {errorMessage}",
                    HttpStatusCode.Conflict => $"{serviceName}: {errorMessage}",
                    HttpStatusCode.InternalServerError => $"{serviceName}: Server error occurred",
                    HttpStatusCode.ServiceUnavailable => $"{serviceName}: Service temporarily unavailable",
                    _ => $"{serviceName}: Request failed ({response.StatusCode})"
                };

                _toastService.ShowError(userMessage);
            }

            return response;
        }
        catch (HttpRequestException ex)
        {
            var serviceName = GetServiceName(request.RequestUri);
            _logger.LogError(ex, "Network error calling {Service}", serviceName);
            _toastService.ShowError($"{serviceName}: Network error - service may be unavailable");
            throw;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            var serviceName = GetServiceName(request.RequestUri);
            _logger.LogWarning(ex, "Timeout calling {Service}", serviceName);
            _toastService.ShowWarning($"{serviceName}: Request timed out");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in HTTP request");
            _toastService.ShowError("An unexpected error occurred");
            throw;
        }
    }

    private static async Task<string> GetErrorMessageAsync(HttpResponseMessage response)
    {
        try
        {
            var content = await response.Content.ReadAsStringAsync();
            // Try to extract a meaningful error message, but keep it short
            if (!string.IsNullOrWhiteSpace(content) && content.Length < 200)
            {
                return content.Trim();
            }
            return "An error occurred";
        }
        catch
        {
            return "An error occurred";
        }
    }

    private static string GetServiceName(Uri? requestUri)
    {
        if (requestUri?.Host == null)
            return "Service";

        // Extract service name from hostname (e.g., "documentservice" -> "Document Service")
        var host = requestUri.Host.ToLowerInvariant();
        if (host.Equals("documentservice", StringComparison.OrdinalIgnoreCase) || host.StartsWith("documentservice.", StringComparison.OrdinalIgnoreCase))
            return "Document Service";
        if (host.Equals("ledgerservice", StringComparison.OrdinalIgnoreCase) || host.StartsWith("ledgerservice.", StringComparison.OrdinalIgnoreCase))
            return "Ledger Service";
        if (host.Equals("mortgageapprover", StringComparison.OrdinalIgnoreCase) || host.StartsWith("mortgageapprover.", StringComparison.OrdinalIgnoreCase))
            return "Mortgage Service";
        if (host.Equals("crmservice", StringComparison.OrdinalIgnoreCase) || host.StartsWith("crmservice.", StringComparison.OrdinalIgnoreCase))
            return "CRM Service";

        return "Service";
    }
}
