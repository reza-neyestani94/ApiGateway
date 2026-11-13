namespace ApiGateway.RateLimiting.Sync.Options;

public class SyncOptions
{
    public int SyncIntervalSeconds { get; set; } = 15;
    public bool FullSnapshotOnStartup { get; set; } = true;
    public bool PeriodicFullSnapshotEnabled { get; set; } = true;
    public int PeriodicFullSnapshotIntervalMinutes { get; set; } = 60;
    public int BatchSize { get; set; } = 500;
    public int MaxChangedRowsPerBatch { get; set; } = 5000;
}
