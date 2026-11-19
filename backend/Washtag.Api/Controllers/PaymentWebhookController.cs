using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Washtag.Application.Interfaces;
using Washtag.Domain.Enums;

namespace Washtag.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/public/payments")]
public class PaymentWebhookController : ControllerBase
{
    private readonly IInvoiceStore _invoices;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PaymentWebhookController> _logger;

    public PaymentWebhookController(IInvoiceStore invoices, IConfiguration configuration,
        ILogger<PaymentWebhookController> logger)
    {
        _invoices = invoices;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook(CancellationToken cancellationToken)
    {
        var secret = _configuration["PAYMENT_WEBHOOK_SECRET"];
        if (string.IsNullOrWhiteSpace(secret)) return StatusCode(503, new { message = "Payment webhook is not configured." });

        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(cancellationToken);
        var expected = Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));
        var provided = Request.Headers["X-WashTag-Signature"].FirstOrDefault() ?? string.Empty;
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(provided)))
            return Unauthorized(new { message = "Invalid webhook signature." });

        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch (JsonException) { return BadRequest(new { message = "Invalid payload." }); }

        using (doc)
        {
            var root = doc.RootElement;
            var evt = root.TryGetProperty("event", out var e) ? e.GetString() : null;
            if (evt != "invoice.paid") return Ok(new { accepted = false, reason = "unsupported event" });
            if (!Guid.TryParse(root.TryGetProperty("shopId", out var s) ? s.GetString() : null, out var shopId) ||
                !Guid.TryParse(root.TryGetProperty("invoiceId", out var i) ? i.GetString() : null, out var invoiceId))
                return BadRequest(new { message = "shopId and invoiceId are required." });
            var reference = root.TryGetProperty("reference", out var r) ? r.GetString() : null;
            var settled = await _invoices.SettleAsync(shopId, invoiceId, PaymentMethod.Cash, reference, cancellationToken);
            if (!settled) return Ok(new { accepted = false, reason = "invoice not payable" });
            _logger.LogInformation("Verified payment for shop {ShopId} invoice {InvoiceId}", shopId, invoiceId);
            return Ok(new { accepted = true });
        }
    }
}
