using Microsoft.Extensions.Configuration;
using Washtag.Application.Interfaces;

namespace Washtag.Api.Services;

/// <summary>Builds the public tracking URL from a configured base URL or the current request.</summary>
public class TrackingUrlProvider : ITrackingUrlProvider
{
    private readonly IHttpContextAccessor _accessor;
    private readonly string? _publicBaseUrl;

    public TrackingUrlProvider(IHttpContextAccessor accessor, IConfiguration configuration)
    {
        _accessor = accessor;
        _publicBaseUrl = configuration["PUBLIC_BASE_URL"]?.Trim();
    }

    public string Build(Guid token)
    {
        if (!string.IsNullOrWhiteSpace(_publicBaseUrl))
            return $"{_publicBaseUrl.TrimEnd('/')}/track/{token}";

        var request = _accessor.HttpContext?.Request;
        if (request == null) return $"http://localhost:8081/track/{token}";

        var forwarded = request.Headers["X-Forwarded-Proto"].FirstOrDefault();
        var scheme = forwarded is "http" or "https" ? forwarded : request.Scheme;
        // Use the parsed host/port instead of the raw header so a crafted Host value
        // cannot inject a path or credentials into a printed QR URL.
        var host = request.Host.Host;
        var port = request.Host.Port;
        var authority = port.HasValue && port is not (80 or 443) ? $"{host}:{port}" : host;
        return $"{scheme}://{authority}/track/{token}";
    }
}
