using Microsoft.SemanticKernel;
using System.Text.Json;
using System.ComponentModel;

namespace CanIHazHouze.AgentService.Services;

/// <summary>
/// Registers HTTP-based tools as Semantic Kernel functions
/// Uses direct HTTP calls to services instead of MCP
/// </summary>
public static class HttpToolRegistration
{
    /// <summary>
    /// Registers LedgerAPI tools as Semantic Kernel functions
    /// </summary>
    public static IKernelBuilder AddLedgerApiTools(
        this IKernelBuilder builder,
        IHttpClientFactory httpClientFactory,
        ILogger logger)
    {
        var httpClient = httpClientFactory.CreateClient();
        
        // Get account info tool
        builder.Plugins.AddFromFunctions("LedgerAPI", new[]
        {
            KernelFunctionFactory.CreateFromMethod(
                async (string owner) =>
                {
                    logger.LogInformation("Calling LedgerAPI.get_account_info for {Owner}", owner);
                    var response = await httpClient.GetAsync($"https+http://ledgerservice/accounts/{owner}");
                    response.EnsureSuccessStatusCode();
                    var content = await response.Content.ReadAsStringAsync();
                    logger.LogInformation("LedgerAPI.get_account_info returned: {Content}", content);
                    return content;
                },
                "get_account_info",
                "Retrieve account information including balance and timestamps",
                new[] { 
                    new KernelParameterMetadata("owner") { 
                        Description = "Username of the account owner",
                        IsRequired = true
                    }
                }
            ),
            
            KernelFunctionFactory.CreateFromMethod(
                async (string owner, decimal amount, string description = "") =>
                {
                    logger.LogInformation("Calling LedgerAPI.update_account_balance for {Owner}, Amount: {Amount}", owner, amount);
                    var requestBody = JsonSerializer.Serialize(new { amount, description });
                    var content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");
                    var response = await httpClient.PostAsync($"https+http://ledgerservice/accounts/{owner}/balance", content);
                    response.EnsureSuccessStatusCode();
                    var result = await response.Content.ReadAsStringAsync();
                    logger.LogInformation("LedgerAPI.update_account_balance returned: {Result}", result);
                    return result;
                },
                "update_account_balance",
                "Update account balance by adding or subtracting the specified amount",
                new[] { 
                    new KernelParameterMetadata("owner") { 
                        Description = "Username of the account owner",
                        IsRequired = true
                    },
                    new KernelParameterMetadata("amount") { 
                        Description = "Amount to add (positive) or subtract (negative)",
                        IsRequired = true
                    },
                    new KernelParameterMetadata("description") { 
                        Description = "Description of the transaction",
                        IsRequired = false
                    }
                }
            ),
            
            KernelFunctionFactory.CreateFromMethod(
                async (string owner, int skip = 0, int take = 10) =>
                {
                    logger.LogInformation("Calling LedgerAPI.get_transaction_history for {Owner}", owner);
                    var response = await httpClient.GetAsync($"https+http://ledgerservice/accounts/{owner}/transactions?skip={skip}&take={take}");
                    response.EnsureSuccessStatusCode();
                    var result = await response.Content.ReadAsStringAsync();
                    logger.LogInformation("LedgerAPI.get_transaction_history returned {Length} chars", result.Length);
                    return result;
                },
                "get_transaction_history",
                "Retrieve transaction history for a user account with pagination support",
                new[] { 
                    new KernelParameterMetadata("owner") { 
                        Description = "Username of the account owner",
                        IsRequired = true
                    },
                    new KernelParameterMetadata("skip") { 
                        Description = "Number of transactions to skip (for pagination)",
                        IsRequired = false
                    },
                    new KernelParameterMetadata("take") { 
                        Description = "Number of transactions to return",
                        IsRequired = false
                    }
                }
            )
        });
        
        logger.LogInformation("Registered 3 LedgerAPI HTTP tools");
        return builder;
    }
}
