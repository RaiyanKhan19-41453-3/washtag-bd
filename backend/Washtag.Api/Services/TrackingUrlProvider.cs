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
        _publicBaseUrl = configuration["PUBLIC_BASE_URL"];
    }

    public string Build(Guid token)
    {
        if (!string.IsNullOrWhiteSpace(_publicBaseUrl))
            return $"{_publicBaseUrl.TrimEnd('/')}/track/{token}";

        var request = _accessor.HttpContext?.Request;
        if (request == null) return $"http://localhost:8081/track/{token}";

        var scheme = request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? request.Scheme;
        return $"{scheme}://{request.Host}/track/{token}";
    }
}
