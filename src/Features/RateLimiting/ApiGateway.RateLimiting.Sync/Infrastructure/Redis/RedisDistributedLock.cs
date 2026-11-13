using StackExchange.Redis;

namespace ApiGateway.RateLimiting.Sync.Infrastructure.Redis;

public sealed class RedisDistributedLock : IAsyncDisposable
{
    private readonly IDatabase _db;
    private readonly ILogger _logger;
    private readonly string _lockKey;
    private readonly string _lockToken;
    private readonly TimeSpan _ttl;
    private readonly TimeSpan _renewInterval;
    private readonly CancellationTokenSource _renewCts = new();
    private readonly Task? _renewTask;

    private bool _disposed;

    private RedisDistributedLock(
        IDatabase db,
        ILogger logger,
        string lockKey,
        string lockToken,
        TimeSpan ttl,
        TimeSpan renewInterval)
    {
        _db = db;
        _logger = logger;
        _lockKey = lockKey;
        _lockToken = lockToken;
        _ttl = ttl;
        _renewInterval = renewInterval;

        _renewTask = Task.Run(RenewLoopAsync);
    }

    public static async Task<RedisDistributedLock?> TryAcquireAsync(
        IDatabase db,
        ILogger logger,
        string lockKey,
        TimeSpan ttl,
        TimeSpan renewInterval,
        CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid().ToString("N");

        var acquired = await db.StringSetAsync(
            lockKey,
            token,
            ttl,
            When.NotExists);

        if (!acquired)
            return null;

        logger.LogDebug("Distributed lock acquired. LockKey={LockKey}, Token={Token}", lockKey, token);

        return new RedisDistributedLock(
            db,
            logger,
            lockKey,
            token,
            ttl,
            renewInterval);
    }

    private async Task RenewLoopAsync()
    {
        while (!_renewCts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_renewInterval, _renewCts.Token);

                var tran = _db.CreateTransaction();
                tran.AddCondition(Condition.StringEqual(_lockKey, _lockToken));

                _ = tran.KeyExpireAsync(_lockKey, _ttl);

                var renewed = await tran.ExecuteAsync();

                if (!renewed)
                {
                    _logger.LogWarning(
                        "Failed to renew distributed lock because token does not match. LockKey={LockKey}",
                        _lockKey);

                    break;
                }

                _logger.LogTrace("Distributed lock renewed. LockKey={LockKey}", _lockKey);
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while renewing distributed lock. LockKey={LockKey}", _lockKey);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            await _renewCts.CancelAsync();

            if (_renewTask != null)
            {
                try
                {
                    await _renewTask;
                }
                catch
                {
                    // ignored
                }
            }

            var tran = _db.CreateTransaction();
            tran.AddCondition(Condition.StringEqual(_lockKey, _lockToken));

            _ = tran.KeyDeleteAsync(_lockKey);

            var released = await tran.ExecuteAsync();

            if (released)
            {
                _logger.LogDebug("Distributed lock released. LockKey={LockKey}", _lockKey);
            }
            else
            {
                _logger.LogWarning(
                    "Distributed lock was not released because token does not match. LockKey={LockKey}",
                    _lockKey);
            }
        }
        finally
        {
            _renewCts.Dispose();
        }
    }
}
