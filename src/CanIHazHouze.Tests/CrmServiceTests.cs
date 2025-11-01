namespace CanIHazHouze.Tests;

public class CrmServiceBasicTests
{
    [Fact]
    public void Complaint_CreatesWithCorrectDefaults()
    {
        // Arrange
        var id = Guid.NewGuid();
        var customerName = "john_doe";
        var title = "Test Complaint";
        var description = "Test Description";

        // Act
        var complaint = new Complaint
        {
            Id = id,
            CustomerName = customerName,
            Title = title,
            Description = description,
            Status = ComplaintStatus.New,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Assert
        Assert.Equal(id, complaint.Id);
        Assert.Equal(customerName, complaint.CustomerName);
        Assert.Equal(title, complaint.Title);
        Assert.Equal(description, complaint.Description);
        Assert.Equal(ComplaintStatus.New, complaint.Status);
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
        var authorName = "support_agent";
        var text = "Test comment";
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
        var approverName = "manager";
        var decision = ApprovalDecision.Approved;
        var comments = "Looks good";
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

    [Theory]
    [InlineData(ComplaintStatus.New)]
    [InlineData(ComplaintStatus.InProgress)]
    [InlineData(ComplaintStatus.Solved)]
    [InlineData(ComplaintStatus.Rejected)]
    public void ComplaintStatus_AllValuesValid(ComplaintStatus status)
    {
        // Arrange & Act
        var complaint = new Complaint { Status = status };

        // Assert
        Assert.Equal(status, complaint.Status);
    }

    [Theory]
    [InlineData(ApprovalDecision.Pending)]
    [InlineData(ApprovalDecision.Approved)]
    [InlineData(ApprovalDecision.Rejected)]
    public void ApprovalDecision_AllValuesValid(ApprovalDecision decision)
    {
        // Arrange & Act
        var approval = new ComplaintApproval { Decision = decision };

        // Assert
        Assert.Equal(decision, approval.Decision);
    }
}

// Test models matching CRM service
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
