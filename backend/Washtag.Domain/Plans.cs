namespace Washtag.Domain;

public static class Plans
{
    public static readonly string[] All = { "Starter", "Growth", "Pro" };

    public static bool IsValid(string? plan) =>
        !string.IsNullOrWhiteSpace(plan) && All.Contains(plan, StringComparer.OrdinalIgnoreCase);

    public static decimal MonthlyPrice(string? plan) => plan?.Trim().ToLowerInvariant() switch
    {
        "growth" => 800m,
        "pro" => 1000m,
        _ => 500m
    };
}
