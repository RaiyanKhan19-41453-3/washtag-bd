namespace Washtag.Application;

/// <summary>Settings for the periodic background maintenance run.</summary>
public sealed class MaintenanceOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 60;
    public bool RunOnStartup { get; set; } = true;
    public int ChatRetentionDays { get; set; } = 90;
}
