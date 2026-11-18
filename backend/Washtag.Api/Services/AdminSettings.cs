using Washtag.Application.Interfaces;

namespace Washtag.Api.Services;

/// <summary>Platform admins are configured with a comma-separated ADMIN_EMAILS env var.</summary>
public class AdminSettings : IAdminSettings
{
    private readonly HashSet<string> _emails;

    public AdminSettings(IConfiguration configuration)
    {
        _emails = (configuration["ADMIN_EMAILS"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(email => email.ToLowerInvariant())
            .ToHashSet();
    }

    public bool IsAdmin(string email) => _emails.Contains(email.Trim().ToLowerInvariant());
}
