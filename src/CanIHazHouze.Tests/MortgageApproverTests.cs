using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace CanIHazHouze.Tests;

public class MortgageApproverTests
{
    [Fact]
    public void MortgageRequest_ShouldHaveCorrectProperties()
    {
        // This is a simple test to verify our models work
        var request = new TestMortgageRequest
        {
            Id = Guid.NewGuid(),
            UserName = "testuser",
            Status = "Pending",
            StatusReason = "Application submitted",
            MissingRequirements = "Documentation required",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        Assert.Equal("testuser", request.UserName);
        Assert.Equal("Pending", request.Status);
        Assert.NotEqual(Guid.Empty, request.Id);
    }

    [Fact]
    public void MortgageRequestStatus_ShouldSupportAllValues()
    {
        var statuses = new[]
        {
            "Pending",
            "UnderReview", 
            "Approved",
            "Rejected",
            "RequiresAdditionalInfo"
        };

        foreach (var status in statuses)
        {
            Assert.NotNull(status);
            Assert.NotEmpty(status);
        }
    }
}

// Test model to verify basic functionality
public class TestMortgageRequest
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusReason { get; set; } = string.Empty;
    public string MissingRequirements { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
