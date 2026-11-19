using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Washtag.Application;
using Washtag.Application.DTOs;
using Washtag.Application.Interfaces;

namespace Washtag.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/billing")]
public class BillingController : ControllerBase
{
    private readonly IBillingService _billing;
    private readonly BillingOptions _options;

    public BillingController(IBillingService billing, BillingOptions options)
    {
        _billing = billing;
        _options = options;
    }

    private Guid ShopId => Guid.Parse(User.FindFirstValue("shopId")!);

    [HttpGet]
    public async Task<IActionResult> Get() => Ok(await _billing.GetOverviewAsync(ShopId));

    [Authorize(Roles = "Owner")]
    [HttpPost("invoices/{id:guid}/pay")]
    public async Task<IActionResult> Pay(Guid id, [FromBody] MarkInvoicePaidRequest request)
    {
        if (!_options.ManualPaymentEnabled)
            return StatusCode(403, new { message = "Invoice settlement requires platform admin approval." });
        return Ok(await _billing.MarkPaidAsync(ShopId, id, request));
    }

    [Authorize(Roles = "Owner")]
    [HttpPut("plan")]
    public async Task<IActionResult> ChangePlan([FromBody] ChangePlanRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return Ok(await _billing.ChangePlanAsync(ShopId, request));
    }
}
