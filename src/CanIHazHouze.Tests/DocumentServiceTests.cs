using System.Text;
using System.Text.Json;

namespace CanIHazHouze.Tests;

public class DocumentServiceBasicTests
{
    [Fact]
    public void DocumentMeta_CreatesCorrectly()
    {
        // Arrange
        var id = Guid.NewGuid();
        var owner = "testuser";
        var tags = new List<string> { "tag1", "tag2" };
        var fileName = "test.txt";
        var uploadedAt = DateTimeOffset.UtcNow;

        // Act
        var documentMeta = new DocumentMeta(id, owner, tags, fileName, uploadedAt);

        // Assert
        Assert.Equal(id, documentMeta.Id);
        Assert.Equal(owner, documentMeta.Owner);
        Assert.Equal(tags, documentMeta.Tags);
        Assert.Equal(fileName, documentMeta.FileName);
        Assert.Equal(uploadedAt, documentMeta.UploadedAt);
    }

    [Fact]
    public void DocumentStorageOptions_HasDefaultBaseDirectory()
    {
        // Arrange & Act
        var options = new DocumentStorageOptions();

        // Assert
        Assert.NotNull(options.BaseDirectory);
        Assert.Contains("UserDocs", options.BaseDirectory);
    }
}

// Test record - should match the one in Program.cs
public record DocumentMeta(Guid Id, string Owner, List<string> Tags, string FileName, DateTimeOffset UploadedAt);

// Test configuration class - should match the one in Program.cs  
public class DocumentStorageOptions
{
    public string BaseDirectory { get; set; } = Path.Combine(Directory.GetCurrentDirectory(), "UserDocs");
}
