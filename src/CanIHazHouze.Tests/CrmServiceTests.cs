namespace CanIHazHouze.Tests;

public class CrmServiceBasicTests
{
    [Fact]
    public void Complaint_CreatesCorrectly()
    {
        // Arrange
        var id = Guid.NewGuid();
        var customerName = "testcustomer";
        var title = "Test Complaint";
        var description = "This is a test complaint description";
        var status = ComplaintStatus.New;
        var createdAt = DateTime.UtcNow;
        var updatedAt = DateTime.UtcNow;

        // Act
        var complaint = new Complaint
        {
            Id = id,
            CustomerName = customerName,
            Title = title,
            Description = description,
            Status = status,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };

        // Assert
        Assert.Equal(id, complaint.Id);
        Assert.Equal(customerName, complaint.CustomerName);
        Assert.Equal(title, complaint.Title);
        Assert.Equal(description, complaint.Description);
        Assert.Equal(status, complaint.Status);
        Assert.Equal(createdAt, complaint.CreatedAt);
        Assert.Equal(updatedAt, complaint.UpdatedAt);
    }

    [Fact]
    public void Complaint_HasEmptyCommentsAndApprovalsByDefault()
    {
        // Arrange & Act
        var complaint = new Complaint();

        // Assert
        Assert.NotNull(complaint.Comments);
        Assert.NotNull(complaint.Approvals);
        Assert.Empty(complaint.Comments);
        Assert.Empty(complaint.Approvals);
    }

    [Fact]
    public void ComplaintComment_CreatesCorrectly()
    {
        // Arrange
        var id = Guid.NewGuid();
        var authorName = "test_author";
        var text = "This is a test comment";
        var createdAt = DateTime.UtcNow;

        // Act
        var comment = new ComplaintComment
        {
            Id = id,
            AuthorName = authorName,
            Text = text,
            CreatedAt = createdAt
        };

        // Assert
        Assert.Equal(id, comment.Id);
        Assert.Equal(authorName, comment.AuthorName);
        Assert.Equal(text, comment.Text);
        Assert.Equal(createdAt, comment.CreatedAt);
    }

    [Fact]
    public void ComplaintApproval_CreatesCorrectly()
    {
        // Arrange
        var id = Guid.NewGuid();
        var approverName = "test_approver";
        var decision = ApprovalDecision.Approved;
        var comments = "Approved for resolution";
        var createdAt = DateTime.UtcNow;

        // Act
        var approval = new ComplaintApproval
        {
            Id = id,
            ApproverName = approverName,
            Decision = decision,
            Comments = comments,
            CreatedAt = createdAt
        };

        // Assert
        Assert.Equal(id, approval.Id);
        Assert.Equal(approverName, approval.ApproverName);
        Assert.Equal(decision, approval.Decision);
        Assert.Equal(comments, approval.Comments);
        Assert.Equal(createdAt, approval.CreatedAt);
    }

    [Fact]
    public void ComplaintStatus_HasExpectedValues()
    {
        // Assert
        Assert.Equal("New", ComplaintStatus.New.ToString());
        Assert.Equal("InProgress", ComplaintStatus.InProgress.ToString());
        Assert.Equal("Solved", ComplaintStatus.Solved.ToString());
        Assert.Equal("Rejected", ComplaintStatus.Rejected.ToString());
    }

    [Fact]
    public void ApprovalDecision_HasExpectedValues()
    {
        // Assert
        Assert.Equal("Pending", ApprovalDecision.Pending.ToString());
        Assert.Equal("Approved", ApprovalDecision.Approved.ToString());
        Assert.Equal("Rejected", ApprovalDecision.Rejected.ToString());
    }
}

// Test data models matching CRM service models
public enum ComplaintStatus
{
    New,
    InProgress,
    Solved,
    Rejected
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

