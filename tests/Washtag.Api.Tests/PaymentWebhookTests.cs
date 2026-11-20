using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Washtag.Api.Controllers;
using Washtag.Application.Interfaces;
using Xunit;

namespace Washtag.Api.Tests;

public class PaymentWebhookTests
{
    private static PaymentWebhookController Controller(string? secret)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PAYMENT_WEBHOOK_SECRET"] = secret
        }).Build();
        return new PaymentWebhookController(Mock.Of<IInvoiceStore>(), Mock.Of<IAppDbContext>(),
            config, NullLogger<PaymentWebhookController>.Instance);
    }

    private static void WithBody(PaymentWebhookController controller, string body)
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                Request = { Body = new MemoryStream(Encoding.UTF8.GetBytes(body)), ContentLength = Encoding.UTF8.GetByteCount(body) }
            }
        };
    }

    [Fact]
    public async Task MissingSecret_Returns503()
    {
        var controller = Controller(null);
        WithBody(controller, "{}");
        Assert.Equal(503, (await controller.Webhook(default)) is ObjectResult r ? r.StatusCode : null);
    }

    [Fact]
    public async Task TamperedSignature_Returns401()
    {
        const string secret = "test-secret-32-characters-minimum";
        var controller = Controller(secret);
        var body = "{\"event\":\"invoice.paid\"}";
        WithBody(controller, body);
        controller.Request.Headers["X-WashTag-Signature"] = "not-the-signature";
        Assert.IsType<UnauthorizedObjectResult>(await controller.Webhook(default));
    }
}
