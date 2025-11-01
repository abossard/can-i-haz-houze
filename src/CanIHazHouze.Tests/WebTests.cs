using Microsoft.Extensions.Logging;
using System.Net.Http.Json;

namespace CanIHazHouze.Tests;

public class WebTests
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task GetWebResourceRootReturnsOkStatusCode()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.CanIHazHouze_AppHost>(cancellationToken);
        appHost.Services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Debug);
            // Override the logging filters from the app's configuration
            logging.AddFilter(appHost.Environment.ApplicationName, LogLevel.Debug);
            logging.AddFilter("Aspire.", LogLevel.Debug);
            // To output logs to the xUnit.net ITestOutputHelper, consider adding a package from https://www.nuget.org/packages?q=xunit+logging
        });
        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
        });

        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await app.StartAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

        // Act
        var httpClient = app.CreateHttpClient("webfrontend");
        await app.ResourceNotifications.WaitForResourceHealthyAsync("webfrontend", cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        var response = await httpClient.GetAsync("/", cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task GetRecentlyUpdatedAccounts_ReturnsOkWithAccounts()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.CanIHazHouze_AppHost>(cancellationToken);
        appHost.Services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Debug);
            logging.AddFilter(appHost.Environment.ApplicationName, LogLevel.Debug);
            logging.AddFilter("Aspire.", LogLevel.Debug);
        });
        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
        });

        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await app.StartAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

        // Act
        var httpClient = app.CreateHttpClient("ledgerservice");
        await app.ResourceNotifications.WaitForResourceHealthyAsync("ledgerservice", cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        
        // First create some test accounts
        var testUser1 = "test_recent_user_1_" + Guid.NewGuid().ToString("N")[..8];
        var testUser2 = "test_recent_user_2_" + Guid.NewGuid().ToString("N")[..8];
        
        await httpClient.GetAsync($"/accounts/{testUser1}", cancellationToken);
        await Task.Delay(100, cancellationToken); // Small delay to ensure different timestamps
        await httpClient.GetAsync($"/accounts/{testUser2}", cancellationToken);
        
        // Now get recent accounts
        var response = await httpClient.GetAsync("/accounts/recent?take=10", cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var accounts = await response.Content.ReadFromJsonAsync<List<AccountInfo>>(cancellationToken: cancellationToken);
        Assert.NotNull(accounts);
        Assert.NotEmpty(accounts);
        
        // Verify the accounts are ordered by LastUpdatedAt descending
        for (int i = 1; i < accounts.Count; i++)
        {
            Assert.True(accounts[i - 1].LastUpdatedAt >= accounts[i].LastUpdatedAt,
                "Accounts should be ordered by LastUpdatedAt descending");
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task GetRecentTransactions_ReturnsOkWithTransactions()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.CanIHazHouze_AppHost>(cancellationToken);
        appHost.Services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Debug);
            logging.AddFilter(appHost.Environment.ApplicationName, LogLevel.Debug);
            logging.AddFilter("Aspire.", LogLevel.Debug);
        });
        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
        });

        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await app.StartAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

        // Act
        var httpClient = app.CreateHttpClient("ledgerservice");
        await app.ResourceNotifications.WaitForResourceHealthyAsync("ledgerservice", cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        
        // Create a test account and make a transaction
        var testUser = "test_transaction_user_" + Guid.NewGuid().ToString("N")[..8];
        await httpClient.GetAsync($"/accounts/{testUser}", cancellationToken);
        
        var balanceRequest = new { Amount = 100.50m, Description = "Test transaction for recent list" };
        await httpClient.PostAsJsonAsync($"/accounts/{testUser}/balance", balanceRequest, cancellationToken);
        
        // Get recent transactions
        var response = await httpClient.GetAsync("/transactions/recent?take=20", cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var transactions = await response.Content.ReadFromJsonAsync<List<TransactionInfo>>(cancellationToken: cancellationToken);
        Assert.NotNull(transactions);
        Assert.NotEmpty(transactions);
        
        // Verify the transactions are ordered by CreatedAt descending
        for (int i = 1; i < transactions.Count; i++)
        {
            Assert.True(transactions[i - 1].CreatedAt >= transactions[i].CreatedAt,
                "Transactions should be ordered by CreatedAt descending");
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task RecentActivityEndpoints_IntegrationTest()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.CanIHazHouze_AppHost>(cancellationToken);
        appHost.Services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Debug);
            logging.AddFilter(appHost.Environment.ApplicationName, LogLevel.Debug);
            logging.AddFilter("Aspire.", LogLevel.Debug);
        });
        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
        });

        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await app.StartAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

        // Act
        var httpClient = app.CreateHttpClient("ledgerservice");
        await app.ResourceNotifications.WaitForResourceHealthyAsync("ledgerservice", cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        
        // Create multiple test users and transactions
        var testUsers = new[] 
        { 
            "test_integration_user_1_" + Guid.NewGuid().ToString("N")[..8],
            "test_integration_user_2_" + Guid.NewGuid().ToString("N")[..8],
            "test_integration_user_3_" + Guid.NewGuid().ToString("N")[..8]
        };
        
        foreach (var user in testUsers)
        {
            // Create account
            await httpClient.GetAsync($"/accounts/{user}", cancellationToken);
            
            // Make a transaction
            var balanceRequest = new { Amount = 50.00m, Description = $"Integration test transaction for {user}" };
            await httpClient.PostAsJsonAsync($"/accounts/{user}/balance", balanceRequest, cancellationToken);
            
            await Task.Delay(50, cancellationToken); // Small delay between operations
        }
        
        // Get recent accounts
        var accountsResponse = await httpClient.GetAsync("/accounts/recent?take=5", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, accountsResponse.StatusCode);
        
        var accounts = await accountsResponse.Content.ReadFromJsonAsync<List<AccountInfo>>(cancellationToken: cancellationToken);
        Assert.NotNull(accounts);
        Assert.NotEmpty(accounts);
        
        // Get recent transactions
        var transactionsResponse = await httpClient.GetAsync("/transactions/recent?take=10", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, transactionsResponse.StatusCode);
        
        var transactions = await transactionsResponse.Content.ReadFromJsonAsync<List<TransactionInfo>>(cancellationToken: cancellationToken);
        Assert.NotNull(transactions);
        Assert.NotEmpty(transactions);
        
        // Verify at least some of our test users appear in the results
        var testUserOwners = accounts.Where(a => testUsers.Contains(a.Owner)).ToList();
        Assert.NotEmpty(testUserOwners);
    }

    private record AccountInfo(string Owner, decimal Balance, DateTimeOffset CreatedAt, DateTimeOffset LastUpdatedAt);
    private record TransactionInfo(Guid Id, string Owner, decimal Amount, decimal BalanceAfter, string Description, DateTimeOffset CreatedAt);
}
