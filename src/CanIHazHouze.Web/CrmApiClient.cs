namespace CanIHazHouze.Web;

public class CrmApiClient(HttpClient httpClient)
{
    public async Task<Complaint?> CreateComplaintAsync(string customerName, string title, string description, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new CreateComplaintRequest(customerName, title, description);
            var response = await httpClient.PostAsJsonAsync("/complaints", request, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<Complaint>(cancellationToken: cancellationToken);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"CrmAPI Error: {response.StatusCode} - {errorContent}");
                return null;
            }
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"CrmAPI HTTP Error: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"CrmAPI Unexpected Error: {ex.Message}");
            return null;
        }
    }

    public async Task<Complaint[]> GetComplaintsAsync(string customerName, CancellationToken cancellationToken = default)
    {
        try
        {
            var complaints = await httpClient.GetFromJsonAsync<Complaint[]>(
                $"/complaints?customerName={Uri.EscapeDataString(customerName)}", 
                cancellationToken);
            return complaints ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    public async Task<Complaint?> GetComplaintAsync(Guid id, string customerName, CancellationToken cancellationToken = default)
    {
        try
        {
            return await httpClient.GetFromJsonAsync<Complaint>(
                $"/complaints/{id}?customerName={Uri.EscapeDataString(customerName)}", 
                cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<Complaint?> UpdateComplaintStatusAsync(Guid id, string customerName, ComplaintStatus status, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new UpdateStatusRequest(status);
            var response = await httpClient.PutAsJsonAsync(
                $"/complaints/{id}/status?customerName={Uri.EscapeDataString(customerName)}", 
                request, 
                cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<Complaint>(cancellationToken: cancellationToken);
            }
            
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<Complaint?> AddCommentAsync(Guid id, string customerName, string authorName, string text, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new AddCommentRequest(authorName, text);
            var response = await httpClient.PostAsJsonAsync(
                $"/complaints/{id}/comments?customerName={Uri.EscapeDataString(customerName)}", 
                request, 
                cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<Complaint>(cancellationToken: cancellationToken);
            }
            
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<Complaint?> AddApprovalAsync(Guid id, string customerName, string approverName, ApprovalDecision decision, string? comments, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new AddApprovalRequest(approverName, decision, comments);
            var response = await httpClient.PostAsJsonAsync(
                $"/complaints/{id}/approvals?customerName={Uri.EscapeDataString(customerName)}", 
                request, 
                cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<Complaint>(cancellationToken: cancellationToken);
            }
            
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<bool> DeleteComplaintAsync(Guid id, string customerName, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.DeleteAsync(
                $"/complaints/{id}?customerName={Uri.EscapeDataString(customerName)}", 
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }
}

// Data models matching the CRM service
public record CreateComplaintRequest(string CustomerName, string Title, string Description);
public record UpdateStatusRequest(ComplaintStatus Status);
public record AddCommentRequest(string AuthorName, string Text);
public record AddApprovalRequest(string ApproverName, ApprovalDecision Decision, string? Comments);

public enum ComplaintStatus
{
    New,
    InProgress,
    Resolved,
    Closed
}

public enum ApprovalDecision
{
    Pending,
    Approved,
    Rejected
}

public class ComplaintComment
{
    public Guid Id { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class ComplaintApproval
{
    public Guid Id { get; set; }
    public string ApproverName { get; set; } = string.Empty;
    public ApprovalDecision Decision { get; set; }
    public string? Comments { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Complaint
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ComplaintStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<ComplaintComment> Comments { get; set; } = new();
    public List<ComplaintApproval> Approvals { get; set; } = new();
}
