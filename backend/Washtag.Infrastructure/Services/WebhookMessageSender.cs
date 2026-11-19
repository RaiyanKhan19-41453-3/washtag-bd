using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Washtag.Application.Interfaces;

namespace Washtag.Infrastructure.Services;

/// <summary>Posts messages to a provider-agnostic webhook. Failures never break shop operations.</summary>
public sealed class WebhookMessageSender : IMessageSender
{
    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WebhookMessageSender> _logger;

    public WebhookMessageSender(HttpClient http, IConfiguration configuration,
        ILogger<WebhookMessageSender> logger)
    {
        _http = http;
        _configuration = configuration;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_configuration["NOTIFICATION_WEBHOOK_URL"]);

    public async Task SendAsync(string to, string text, CancellationToken cancellationToken = default)
    {
        var url = _configuration["NOTIFICATION_WEBHOOK_URL"]?.Trim();
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(to)) return;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(new { to, text })
            };
            var token = _configuration["NOTIFICATION_WEBHOOK_TOKEN"]?.Trim();
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("Notification webhook returned {Status}", (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Notification webhook delivery failed; shop operation continues.");
        }
    }
}
