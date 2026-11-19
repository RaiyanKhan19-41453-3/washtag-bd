using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Washtag.Application.DTOs;
using Washtag.Application.Interfaces;

namespace Washtag.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orders;
    private readonly IIdempotencyStore _idempotency;

    public OrdersController(IOrderService orders, IIdempotencyStore idempotency)
    {
        _orders = orders;
        _idempotency = idempotency;
    }

    private Guid ShopId => Guid.Parse(User.FindFirstValue("shopId")!);
    private string Actor => User.FindFirstValue(ClaimTypes.Name) ?? "Staff";

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        return Ok(await _orders.GetOrdersAsync(ShopId, status, search, page, pageSize));
    }

    [HttpGet("lookup/{token}")]
    public async Task<IActionResult> Lookup(string token)
    {
        return Ok(await _orders.GetOrderByTokenAsync(ShopId, token));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        return Ok(await _orders.GetOrderAsync(ShopId, id));
    }

    [HttpGet("{id:guid}/qr")]
    public async Task<IActionResult> Qr(Guid id)
    {
        var bytes = await _orders.GetQrPngAsync(ShopId, id);
        return File(bytes, "image/png");
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var key = Request.Headers["Idempotency-Key"].FirstOrDefault();
        var claimed = !string.IsNullOrWhiteSpace(key) &&
            await _idempotency.TryClaimAsync(key!, ShopId, HttpContext.RequestAborted);
        if (!string.IsNullOrWhiteSpace(key) && !claimed)
            return Conflict(new { message = "Duplicate request. This order was already created." });

        try
        {
            var result = await _orders.CreateOrderAsync(ShopId, request, Actor);
            return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
        }
        catch
        {
            if (claimed && !string.IsNullOrWhiteSpace(key))
                await _idempotency.ReleaseAsync(key!, ShopId, HttpContext.RequestAborted);
            throw;
        }
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateStatusRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return Ok(await _orders.UpdateStatusAsync(ShopId, id, request, Actor));
    }

    [HttpPost("{id:guid}/payment")]
    public async Task<IActionResult> RecordPayment(Guid id, [FromBody] RecordPaymentRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return Ok(await _orders.RecordPaymentAsync(ShopId, id, request, Actor));
    }

    [Authorize(Roles = "Owner")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        await _orders.CancelOrderAsync(ShopId, id, Actor);
        return NoContent();
    }
}
