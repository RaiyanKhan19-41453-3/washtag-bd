using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Washtag.Application.DTOs;
using Washtag.Application.Interfaces;

namespace Washtag.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/billing")]
public class BillingController : ControllerBase
{
    private readonly IBillingService _billing;

    public BillingController(IBillingService billing) => _billing = billing;

    private Guid ShopId => Guid.Parse(User.FindFirstValue("shopId")!);

    [HttpGet]
    public async Task<IActionResult> Get() => Ok(await _billing.GetOverviewAsync(ShopId));

    [Authorize(Roles = "Owner")]
    [HttpPost("invoices/{id:guid}/pay")]
    public async Task<IActionResult> Pay(Guid id, [FromBody] MarkInvoicePaidRequest request) =>
        Ok(await _billing.MarkPaidAsync(ShopId, id, request));

    [Authorize(Roles = "Owner")]
    [HttpPut("plan")]
    public async Task<IActionResult> ChangePlan([FromBody] ChangePlanRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return Ok(await _billing.ChangePlanAsync(ShopId, request));
    }
}
