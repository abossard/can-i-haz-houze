using System.ComponentModel;
using ModelContextProtocol.Server;
using CanIHazHouze.CrmService;
using Microsoft.Extensions.Logging;

namespace CanIHazHouze.CrmService.McpTools;

[McpServerToolType]
public class CrmTools
{
    private readonly ICrmService _crmService;

    public CrmTools(ICrmService crmService)
    {
        _crmService = crmService;
    }

    [McpServerTool]
    [Description("Create a new customer complaint in the CRM system")]
    public async Task<Complaint> CreateComplaint(
        [Description("Customer name")] string customerName,
        [Description("Complaint title")] string title,
        [Description("Complaint description")] string description)
    {
        return await _crmService.CreateComplaintAsync(customerName, title, description);
    }

    [McpServerTool]
    [Description("Retrieve all complaints for a specific customer")]
    public async Task<IEnumerable<Complaint>> GetComplaints(
        [Description("Customer name")] string customerName)
    {
        return await _crmService.GetComplaintsAsync(customerName);
    }

    [McpServerTool]
    [Description("Retrieve recent complaints across all customers with configurable limit")]
    public async Task<IEnumerable<Complaint>> GetRecentComplaints(
        [Description("Maximum number of complaints to return")] int limit = 10)
    {
        return await _crmService.GetRecentComplaintsAsync(limit);
    }

    [McpServerTool]
    [Description("Retrieve a specific complaint by ID for a customer")]
    public async Task<Complaint?> GetComplaint(
        [Description("Complaint ID")] string id,
        [Description("Customer name")] string customerName)
    {
        return await _crmService.GetComplaintAsync(Guid.Parse(id), customerName);
    }

    [McpServerTool]
    [Description("Update the status of a complaint (New, InProgress, Solved, Rejected)")]
    public async Task<Complaint?> UpdateComplaintStatus(
        [Description("Complaint ID")] string id,
        [Description("Customer name")] string customerName,
        [Description("New status")] ComplaintStatus status)
    {
        return await _crmService.UpdateComplaintStatusAsync(Guid.Parse(id), customerName, status);
    }

    [McpServerTool]
    [Description("Add a support comment to an existing complaint")]
    public async Task<Complaint?> AddComplaintComment(
        [Description("Complaint ID")] string id,
        [Description("Customer name")] string customerName,
        [Description("Author name")] string authorName,
        [Description("Comment text")] string text)
    {
        return await _crmService.AddCommentAsync(Guid.Parse(id), customerName, authorName, text);
    }

    [McpServerTool]
    [Description("Add an approval decision to a complaint (Pending, Approved, Rejected)")]
    public async Task<Complaint?> AddComplaintApproval(
        [Description("Complaint ID")] string id,
        [Description("Customer name")] string customerName,
        [Description("Approver name")] string approverName,
        [Description("Decision")] ApprovalDecision decision,
        [Description("Optional comments")] string? comments)
    {
        return await _crmService.AddApprovalAsync(Guid.Parse(id), customerName, approverName, decision, comments);
    }

    [McpServerTool]
    [Description("Permanently delete a complaint and all associated data")]
    public async Task<bool> DeleteComplaint(
        [Description("Complaint ID")] string id,
        [Description("Customer name")] string customerName)
    {
        return await _crmService.DeleteComplaintAsync(Guid.Parse(id), customerName);
    }
}
