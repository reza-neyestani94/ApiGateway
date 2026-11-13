namespace ApiGateway.RateLimiting.Sync.Options;

public class RedisOptions
{
    public string Connection { get; set; } = "";
    public string LockKey { get; set; } = "rl:policy:lock";
    public int LockTtlSeconds { get; set; } = 120;
    public int LockRenewIntervalSeconds { get; set; } = 30;
}