using Washtag.Application.Interfaces;

namespace Washtag.Api.Services;

/// <summary>Builds the public tracking URL from the current request origin.</summary>
public class TrackingUrlProvider : ITrackingUrlProvider
{
    private readonly IHttpContextAccessor _accessor;

    public TrackingUrlProvider(IHttpContextAccessor accessor) => _accessor = accessor;

    public string Build(Guid token)
    {
        var request = _accessor.HttpContext?.Request;
        if (request == null) return $"http://localhost:8081/track/{token}";

        var scheme = request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? request.Scheme;
        return $"{scheme}://{request.Host}/track/{token}";
    }
}
