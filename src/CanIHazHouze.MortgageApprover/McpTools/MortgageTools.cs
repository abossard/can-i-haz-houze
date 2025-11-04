using System.ComponentModel;
using ModelContextProtocol.Server;
using CanIHazHouze.MortgageApprover;
using Microsoft.Extensions.Logging;

namespace CanIHazHouze.MortgageApprover.McpTools;

[McpServerToolType]
public class MortgageTools
{
    private readonly IMortgageApprovalService _mortgageService;
    private readonly ICrossServiceVerificationService _verificationService;

    public MortgageTools(
        IMortgageApprovalService mortgageService,
        ICrossServiceVerificationService verificationService)
    {
        _mortgageService = mortgageService;
        _verificationService = verificationService;
    }

    [McpServerTool]
    [Description("Create a new mortgage application request")]
    public async Task<MortgageRequestDto> CreateMortgageRequest(
        [Description("Username for the mortgage applicant")] string userName)
    {
        var mortgageRequest = await _mortgageService.CreateMortgageRequestAsync(userName);
        return MortgageRequestDto.FromDomain(mortgageRequest);
    }

        [McpServerTool]
    [Description("Retrieve mortgage application details by request ID")]
    public async Task<MortgageRequest?> GetMortgageRequest([Description("Request ID")] string requestId)
    {
        return await _mortgageService.GetMortgageRequestAsync(Guid.Parse(requestId));
    }

    [McpServerTool]
    [Description("Retrieve mortgage application details by username")]
    public async Task<MortgageRequestDto?> GetMortgageRequestByUser(
        [Description("Username of the mortgage applicant")] string userName)
    {
        var mortgageRequest = await _mortgageService.GetMortgageRequestByUserAsync(userName);
        return mortgageRequest != null ? MortgageRequestDto.FromDomain(mortgageRequest) : null;
    }

    [McpServerTool]
    [Description("Updates the mortgage data fields (income, credit, employment, property) for an existing mortgage request")]
    public async Task<MortgageRequestDto?> UpdateMortgageData(
        [Description("The mortgage request ID")] string requestId,
        [Description("Income data (optional)")] MortgageIncomeData? income,
        [Description("Credit data (optional)")] MortgageCreditData? credit,
        [Description("Employment data (optional)")] MortgageEmploymentData? employment,
        [Description("Property data (optional)")] MortgagePropertyData? property)
    {
        var updateData = new UpdateMortgageDataStrongDto
        {
            Income = income,
            Credit = credit,
            Employment = employment,
            Property = property
        };
        
        var mortgageRequest = await _mortgageService.UpdateMortgageDataStrongAsync(Guid.Parse(requestId), updateData);
        return mortgageRequest != null ? MortgageRequestDto.FromDomain(mortgageRequest) : null;
    }

    [McpServerTool]
    [Description("Trigger cross-service verification for mortgage request including document and ledger checks")]
    public async Task<object> VerifyMortgageRequest([Description("Request ID")] string requestId)
    {
        var request = await _mortgageService.GetMortgageRequestAsync(Guid.Parse(requestId));
        if (request == null)
        {
            return new { Success = false, Error = $"Mortgage request {requestId} not found" };
        }
        
        var verification = await _verificationService.VerifyMortgageRequirementsAsync(request.UserName, request.RequestData);
        return new { Success = true, Verification = verification };
    }
}
