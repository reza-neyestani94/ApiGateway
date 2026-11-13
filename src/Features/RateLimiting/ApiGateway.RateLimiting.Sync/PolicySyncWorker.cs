using ApiGateway.Shared.Domain.Enums;
using ApiGateway.RateLimiting.Core.Helpers;
using ApiGateway.RateLimiting.Core.Models;
using ApiGateway.RateLimiting.Persistence;
using ApiGateway.RateLimiting.Persistence.Entities;
using ApiGateway.RateLimiting.Sync.Infrastructure.Redis;
using ApiGateway.RateLimiting.Sync.Models.Internal;
using ApiGateway.RateLimiting.Sync.Options;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;


namespace ApiGateway.RateLimiting.Sync;

public sealed class PolicySyncWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<PolicySyncWorker> _logger;
    private readonly RedisOptions _redisOptions;
    private readonly SyncOptions _syncOptions;

    private const string CurrentSchemaVersion = "v4";
    private const string PolicyIndexKey = "rl:policy:index";
    private const string PolicyVersionKey = "rl:policy:version";
    private const string PolicyHealthKey = "rl:policy:health";
    private const string PolicyWatermarkKey = "rl:policy:watermark";
    private const string ModeFullSnapshot = "FullSnapshot";
    private const string SkipReasonDistributedLockNotAcquired = "DistributedLockNotAcquired";
    private const string StatusOk = "OK";
    private const string ModeIncremental = "RowVersionIncremental";
    private const string SyncStatusError = "ERROR";
    private const string SyncStatusSkipped = "SKIPPED";


    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private DateTime? _lastFullSnapshotUtc;

    public PolicySyncWorker(
        IServiceProvider serviceProvider,
        IConnectionMultiplexer redis,
        ILogger<PolicySyncWorker> logger,
        IOptions<RedisOptions> redisOptions,
        IOptions<SyncOptions> syncOptions)
    {
        _serviceProvider = serviceProvider;
        _redis = redis;
        _logger = logger;
        _redisOptions = redisOptions.Value;
        _syncOptions = syncOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Log service startup and the current Redis payload schema version.
        _logger.LogInformation(
            "PolicySyncService production-grade started. SchemaVersion={SchemaVersion}",
            CurrentSchemaVersion);

        // Get a Redis database instance used for all sync operations.
        var redisDb = _redis.GetDatabase();

        // Convert configured sync interval (seconds) to a TimeSpan.
        var interval = TimeSpan.FromSeconds(_syncOptions.SyncIntervalSeconds);

        // Track consecutive failures to apply exponential backoff on repeated errors.
        var failureCount = 0;

        // Cap retry delay to avoid unbounded backoff.
        var maxBackoff = TimeSpan.FromMinutes(5);

        // Optionally build a complete Redis snapshot when the service starts.
        if (_syncOptions.FullSnapshotOnStartup)
        {
            try
            {
                // Execute startup full snapshot and capture metrics/health information.
                await ExecuteWithMetricsAsync(
                    redisDb,
                    mode: "FullSnapshotStartup",
                    action: ct => RunFullSnapshotAsync(redisDb, ct),
                    stoppingToken);

                // Reset failure counter after a successful run.
                failureCount = 0;

                // Remember when the last full snapshot completed successfully.
                _lastFullSnapshotUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                // Log startup failure but keep the worker alive so it can retry in the main loop.
                failureCount++;
                _logger.LogError(ex, "Startup full snapshot failed.");
            }
        }

        // Keep syncing until the host requests shutdown.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (ShouldRunPeriodicFullSnapshot())
                {
                    // Run a scheduled full snapshot and update the last successful full snapshot timestamp.
                    await ExecuteWithMetricsAsync(
                        redisDb,
                        mode: "PeriodicFullSnapshot",
                        action: ct => RunFullSnapshotAsync(redisDb, ct),
                        stoppingToken);

                    _lastFullSnapshotUtc = DateTime.UtcNow;
                }
                else
                {
                    // Run lightweight incremental synchronization based on SQL RowVersion watermark.
                    await ExecuteWithMetricsAsync(
                        redisDb,
                        mode: "RowVersionIncremental",
                        action: ct => RunIncrementalAsync(redisDb, ct),
                        stoppingToken);
                }

                // Clear consecutive failure count after a successful sync iteration.
                failureCount = 0;

                // Wait until the next sync interval, unless shutdown is requested.
                await Task.Delay(interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Expected during graceful shutdown; no error logging needed.
            }
            catch (Exception ex)
            {
                // Increase failure count so retry delay grows on repeated errors.
                failureCount++;

                // Use exponential backoff, capped to avoid excessively large retry factors.
                var factor = Math.Pow(2, Math.Min(failureCount, 10));

                // Calculate retry delay and cap it at the configured maximum backoff.
                var delay = TimeSpan.FromMilliseconds(
                    Math.Min(maxBackoff.TotalMilliseconds, interval.TotalMilliseconds * factor));

                // Log the failure together with retry/backoff diagnostics.
                _logger.LogError(
                    ex,
                    "Policy sync loop failed. FailureCount={FailureCount}, Backoff={Backoff}",
                    failureCount,
                    delay);

                // Publish error health information so external systems can detect sync failures.
                await WriteHealthAsync(redisDb, new
                {
                    status = "ERROR",
                    schemaVersion = CurrentSchemaVersion,
                    lastErrorUtc = DateTime.UtcNow,
                    lastError = ex.Message,
                    failureCount,
                    backoffMs = delay.TotalMilliseconds
                });

                try
                {
                    // Wait before retrying, unless the host is shutting down.
                    await Task.Delay(delay, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    // Expected during graceful shutdown; no error logging needed.
                }
            }
        }
    }

    private bool ShouldRunPeriodicFullSnapshot()
    {
        // Skip periodic full snapshots entirely if the feature is disabled in configuration.
        if (!_syncOptions.PeriodicFullSnapshotEnabled)
            return false;

        // If no successful full snapshot has been recorded yet, force one now.
        if (_lastFullSnapshotUtc == null)
            return true;

        // Convert the configured periodic full snapshot interval from minutes to TimeSpan.
        var interval = TimeSpan.FromMinutes(_syncOptions.PeriodicFullSnapshotIntervalMinutes);

        // Run a new full snapshot only if the configured interval has elapsed since the last one.
        return DateTime.UtcNow - _lastFullSnapshotUtc.Value >= interval;
    }

    /// <summary>
    /// Attempts to run a full snapshot while holding a Redis distributed lock.
    /// If the lock cannot be acquired, the operation is skipped and skipped metrics are returned.
    /// </summary>
    private async Task<PolicySyncMetrics> RunFullSnapshotAsync(IDatabase redisDb, CancellationToken ct)
    {
        // Acquire the distributed lock and release it automatically when leaving this scope.
        await using var distributedLock = await AcquireLockOrSkipAsync(redisDb, ct);

        // If the lock is not available, another instance is already running the sync.
        if (distributedLock == null)
        {
            // Lock contention is not an error; return skipped metrics instead.
            return CreateSkippedMetrics(ModeFullSnapshot, SkipReasonDistributedLockNotAcquired);
        }

        // Execute the actual full snapshot logic while the distributed lock is held.
        return await RunFullSnapshotCoreAsync(redisDb, ct);
    }

    /// <summary>
    /// Executes the core full snapshot logic by rebuilding Redis policy groups from SQL Server.
    /// </summary>
    /// <returns>
    /// Metrics describing the completed full snapshot operation.
    /// </returns>
    private async Task<PolicySyncMetrics> RunFullSnapshotCoreAsync(IDatabase redisDb, CancellationToken ct)
    {
        // Create a scoped service provider because AppDbContext is registered as scoped.
        using var scope = _serviceProvider.CreateScope();

        // Resolve a fresh DbContext instance for this full snapshot operation.
        var dbContext = scope.ServiceProvider.GetRequiredService<RateLimitingDbContext>();

        // Capture the start time in UTC for metrics and observability.
        var started = DateTime.UtcNow;

        // Load all active, non-deleted policies using a deterministic order.
        // Higher priority policies are placed first within each group.
        var allActivePolicies = await dbContext.RateLimitPolicies
            .AsNoTracking()
            .Where(p => p.IsActive && !p.IsDeleted)
            .OrderBy(p => p.ServiceName)
            .ThenBy(p => p.SubjectType)
            .ThenBy(p => p.SubjectKey)
            .ThenByDescending(p => p.Priority)
            .ThenBy(p => p.HttpMethodType)
            .ThenBy(p => p.EndpointPattern)
            .ToListAsync(ct);

        // Group policies by their immutable Redis identity: service + subject type + subject key.
        var grouped = allActivePolicies
            .GroupBy(p => new GroupKey(p.ServiceName, p.SubjectType, p.SubjectKey))
            .ToList();

        // Build the set of Redis policy keys that should exist after this snapshot.
        var currentKeys = grouped
            .Select(g => BuildPolicyKey(g.Key.ServiceName, g.Key.SubjectType, g.Key.SubjectKey))
            .ToHashSet(StringComparer.Ordinal);

        // Read the existing Redis policy key index to detect orphaned policy groups.
        var existingKeys = (await redisDb.SetMembersAsync(PolicyIndexKey))
            .Select(x => (string)x!)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);

        // Delete Redis keys that are no longer present in the SQL snapshot.
        var orphanDeleted = 0;

        foreach (var orphanKey in existingKeys.Except(currentKeys))
        {
            ct.ThrowIfCancellationRequested();

            // Remove the orphaned policy payload.
            await redisDb.KeyDeleteAsync(orphanKey);

            // Remove the orphaned key from the Redis index as well.
            await redisDb.SetRemoveAsync(PolicyIndexKey, orphanKey);

            orphanDeleted++;
        }

        // Write each policy group to Redis using a single snapshot timestamp.
        var written = 0;
        var now = DateTime.UtcNow;

        foreach (var group in grouped)
        {
            ct.ThrowIfCancellationRequested();

            await WriteGroupAsync(
                redisDb,
                group.Key.ServiceName,
                group.Key.SubjectType,
                group.Key.SubjectKey,
                group.ToList(),
                now);

            written++;
        }

        // Store the maximum SQL RowVersion as the new incremental sync watermark.
        var maxRowVersion = await GetMaxRowVersionAsync(dbContext, ct);

        if (maxRowVersion.Length > 0)
        {
            await SaveWatermarkAsync(redisDb, maxRowVersion);
        }

        // Capture the finish time after all Redis writes and watermark updates are completed.
        var finished = DateTime.UtcNow;

        // Build structured metrics for observability, monitoring, and health/version reporting.
        var metrics = new PolicySyncMetrics
        {
            Mode = ModeFullSnapshot,
            Status = StatusOk,
            SchemaVersion = CurrentSchemaVersion,
            StartedAtUtc = started,
            FinishedAtUtc = finished,
            DurationMs = (finished - started).TotalMilliseconds,
            WrittenGroups = written,
            OrphanDeletedGroups = orphanDeleted,
            Watermark = maxRowVersion.Length > 0
                ? RowVersionHelper.ToHex(maxRowVersion)
                : null
        };

        // Publish the latest sync version/health information to Redis.
        await WriteVersionAsync(redisDb, metrics);

        return metrics;
    }

    private async Task<PolicySyncMetrics> RunIncrementalAsync(IDatabase redisDb, CancellationToken ct)
    {
        // Acquire the distributed lock so only one instance can perform the incremental sync.
        await using var distributedLock = await AcquireLockOrSkipAsync(redisDb, ct);
        if (distributedLock == null)
        {
            // Lock contention is expected in multi-instance deployments and is not treated as an error.
            return CreateSkippedMetrics(ModeIncremental, SkipReasonDistributedLockNotAcquired);
        }

        // Create a scoped DbContext for this sync execution.
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RateLimitingDbContext>();

        // Capture the start time for metrics and health reporting.
        var started = DateTime.UtcNow;

        // Load the last persisted SQL RowVersion watermark from Redis.
        var watermark = await LoadWatermarkAsync(redisDb);

        // Incremental sync requires a persisted watermark.
        // If no watermark exists, rebuild Redis state from scratch while reusing the current lock.
        if (watermark == null || string.IsNullOrWhiteSpace(watermark.LastRowVersionHex))
        {
            _logger.LogInformation("No watermark found. Running full snapshot instead of incremental.");

            return await RunFullSnapshotCoreAsync(redisDb, ct);
        }

        // Convert the persisted watermark from hex to the SQL rowversion byte representation.
        var lastRowVersion = RowVersionHelper.FromHex(watermark.LastRowVersionHex);

        // Load a bounded batch of rows that changed after the last synced RowVersion.
        var changedRows = await GetChangedPoliciesAsync(
            dbContext,
            lastRowVersion,
            _syncOptions.MaxChangedRowsPerBatch,
            ct);

        // If nothing changed, publish a successful no-op result.
        if (changedRows.Count == 0)
        {
            var finishedNoChange = DateTime.UtcNow;

            var noChangeMetrics = new PolicySyncMetrics
            {
                Mode = ModeIncremental,
                Status = StatusOk,
                SchemaVersion = CurrentSchemaVersion,
                StartedAtUtc = started,
                FinishedAtUtc = finishedNoChange,
                DurationMs = (finishedNoChange - started).TotalMilliseconds,
                ChangedRows = 0,
                ImpactedGroups = 0,
                UpdatedGroups = 0,
                DeletedGroups = 0,
                Watermark = watermark.LastRowVersionHex
            };

            await WriteVersionAsync(redisDb, noChangeMetrics);
            return noChangeMetrics;
        }

        // Determine which Redis groups are affected by the changed SQL rows.
        var impactedGroups = changedRows
            .Select(p => new GroupKey(p.ServiceName, p.SubjectType, p.SubjectKey))
            .Distinct()
            .ToList();

        var updatedGroups = 0;
        var deletedGroups = 0;

        // Use one shared timestamp for all writes in this incremental batch.
        var now = DateTime.UtcNow;

        // Rebuild each impacted Redis group from the current SQL state.
        foreach (var group in impactedGroups)
        {
            ct.ThrowIfCancellationRequested();

            var currentGroupPolicies = await dbContext.RateLimitPolicies
                .AsNoTracking()
                .Where(p =>
                    p.ServiceName == group.ServiceName &&
                    p.SubjectType == group.SubjectType &&
                    p.SubjectKey == group.SubjectKey &&
                    p.IsActive &&
                    !p.IsDeleted)
                .OrderByDescending(p => p.Priority)
                .ThenBy(p => p.HttpMethodType)
                .ThenBy(p => p.EndpointPattern)
                .ToListAsync(ct);

            var redisKey = BuildPolicyKey(
                group.ServiceName,
                group.SubjectType,
                group.SubjectKey);

            if (currentGroupPolicies.Count == 0)
            {
                // The group no longer contains any active policy, so remove it from Redis and from the index.
                await redisDb.KeyDeleteAsync(redisKey);
                await redisDb.SetRemoveAsync(PolicyIndexKey, redisKey);

                deletedGroups++;

                _logger.LogInformation(
                    "Deleted policy group because no active policies remain. Key={Key}",
                    redisKey);
            }
            else
            {
                // Rewrite the full payload of the impacted group in Redis.
                await WriteGroupAsync(
                    redisDb,
                    group.ServiceName,
                    group.SubjectType,
                    group.SubjectKey,
                    currentGroupPolicies,
                    now);

                updatedGroups++;
            }
        }

        // Advance the watermark to the highest RowVersion that was fully processed in this batch.
        var maxChangedRowVersion = changedRows
            .Select(x => x.RowVersion)
            .Aggregate((max, current) => RowVersionHelper.Max(max, current));

        await SaveWatermarkAsync(redisDb, maxChangedRowVersion);

        var finished = DateTime.UtcNow;

        // Build structured metrics for monitoring and health/version reporting.
        var metrics = new PolicySyncMetrics
        {
            Mode = ModeIncremental,
            Status = StatusOk,
            SchemaVersion = CurrentSchemaVersion,
            StartedAtUtc = started,
            FinishedAtUtc = finished,
            DurationMs = (finished - started).TotalMilliseconds,
            ChangedRows = changedRows.Count,
            ImpactedGroups = impactedGroups.Count,
            UpdatedGroups = updatedGroups,
            DeletedGroups = deletedGroups,
            Watermark = RowVersionHelper.ToHex(maxChangedRowVersion)
        };

        // Publish the latest sync result to Redis for health and version tracking.
        await WriteVersionAsync(redisDb, metrics);

        return metrics;
    }


    private async Task<RedisDistributedLock?> AcquireLockOrSkipAsync(IDatabase redisDb, CancellationToken ct)
    {
        // Convert configured lock timing values to TimeSpan instances.
        var ttl = TimeSpan.FromSeconds(_redisOptions.LockTtlSeconds);
        var renewInterval = TimeSpan.FromSeconds(_redisOptions.LockRenewIntervalSeconds);

        // Try to acquire the distributed Redis lock that serializes sync execution
        // across multiple service instances.
        var distributedLock = await RedisDistributedLock.TryAcquireAsync(
            redisDb,
            _logger,
            _redisOptions.LockKey,
            ttl,
            renewInterval,
            ct);

        if (distributedLock == null)
        {
            // Another instance already holds the lock, so this sync attempt is skipped.
            // This is expected behavior in multi-instance deployments.
            _logger.LogDebug("Policy sync skipped because distributed lock is held by another instance.");
        }

        return distributedLock;
    }

    /// <summary>
    /// Loads a bounded batch of policies changed after the specified rowversion watermark,
    /// ordered by rowversion ascending so the caller can safely advance the watermark.
    /// </summary>
    private async Task<List<RateLimitPolicy>> GetChangedPoliciesAsync(RateLimitingDbContext dbContext, byte[] lastRowVersion, int maxRows, CancellationToken ct)
    {
        if (lastRowVersion is null || lastRowVersion.Length == 0)
            throw new ArgumentException("RowVersion watermark is required.", nameof(lastRowVersion));

        if (maxRows <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRows), "Max rows must be greater than zero.");

        // Parameterize the last processed rowversion watermark.
        var rowVersionParam = new SqlParameter("@lastRowVersion", lastRowVersion);

        // Limit the size of each incremental sync batch.
        var maxRowsParam = new SqlParameter("@maxRows", maxRows);

        return await dbContext.RateLimitPolicies
            .FromSqlRaw("""
            SELECT TOP (@maxRows)
                Id,
                PolicyName,
                ServiceName,
                HttpMethodType,
                EndpointPattern,
                SubjectType,
                SubjectKey,
                LimitCount,
                WindowSize,
                WindowUnit,
                AlgorithmType,
                Priority,
                EffectiveFrom,
                EffectiveTo,
                IsActive,
                IsDeleted,
                Notes,
                CreatedAt,
                UpdatedAt,
                UpdatedBy,
                RowVersion
            FROM dbo.RateLimitPolicies
            WHERE RowVersion > @lastRowVersion
            ORDER BY RowVersion ASC
            """, maxRowsParam, rowVersionParam)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    /// <summary>
    /// Returns the highest SQL Server rowversion currently present in the rate-limit policy table.
    /// Returns an empty byte array when the table contains no rows.
    /// </summary>
    private async Task<byte[]> GetMaxRowVersionAsync(RateLimitingDbContext dbContext, CancellationToken ct)
    {
        // Access the underlying database connection managed by EF Core.
        var connection = dbContext.Database.GetDbConnection();

        // Open the connection if it is not already open.
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(ct);

        try
        {
            // Create a lightweight scalar command to fetch the current maximum rowversion.
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT MAX(RowVersion) FROM dbo.RateLimitPolicies";

            var result = await command.ExecuteScalarAsync(ct);

            // An empty table produces NULL, so return an empty byte array as "no watermark".
            if (result == null || result == DBNull.Value)
                return Array.Empty<byte>();

            // SQL Server rowversion is materialized as byte[].
            return (byte[])result;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    /// <summary>
    /// Loads the last successfully persisted policy sync watermark from Redis.
    /// Returns null when no valid watermark is available.
    /// </summary>
    private async Task<PolicySyncWatermark?> LoadWatermarkAsync(IDatabase redisDb)
    {
        // Read the persisted watermark JSON from Redis.
        var raw = await redisDb.StringGetAsync(PolicyWatermarkKey);

        // No watermark means incremental sync cannot safely determine where to resume.
        if (raw.IsNullOrEmpty)
            return null;

        try
        {
            // Deserialize the Redis JSON payload into the watermark model.
            var watermark = JsonSerializer.Deserialize<PolicySyncWatermark>(
                raw.ToString(),
                _jsonOptions);

            // Treat malformed or incomplete watermark data as missing.
            if (watermark == null || string.IsNullOrWhiteSpace(watermark.LastRowVersionHex))
            {
                _logger.LogWarning(
                    "Policy sync watermark is missing or incomplete. A full snapshot will be required.");

                return null;
            }

            return watermark;
        }
        catch (JsonException ex)
        {
            // Invalid JSON should not permanently break sync. Returning null allows
            // the caller to recover by running a full snapshot.
            _logger.LogWarning(
                ex,
                "Policy sync watermark in Redis is invalid JSON. A full snapshot will be required.");

            return null;
        }
    }

    /// <summary>
    /// Persists the latest successfully synchronized rowversion watermark to Redis.
    /// </summary>
    private async Task SaveWatermarkAsync(IDatabase redisDb, byte[] rowVersion)
    {
        // Build the persisted watermark payload that represents the latest
        // successfully synchronized SQL Server rowversion.
        var payload = new PolicySyncWatermark
        {
            SchemaVersion = CurrentSchemaVersion,
            LastRowVersionHex = RowVersionHelper.ToHex(rowVersion),
            UpdatedAtUtc = DateTime.UtcNow
        };

        // Store the watermark as JSON in Redis so incremental sync can resume later.
        await redisDb.StringSetAsync(
            PolicyWatermarkKey,
            JsonSerializer.Serialize(payload, _jsonOptions));
    }

    private async Task WriteGroupAsync(IDatabase redisDb, string serviceName, SubjectType subjectType, string subjectKey, List<RateLimitPolicy> groupPolicies, DateTime syncedAtUtc)
    {
        // Validate required inputs because this method writes a persisted cache payload.
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name is required.", nameof(serviceName));

        // Subject key is part of the Redis identity and must be non-empty.
        if (string.IsNullOrWhiteSpace(subjectKey))
            throw new ArgumentException("Subject key is required.", nameof(subjectKey));

        // Group policies must be provided by the caller.
        if (groupPolicies == null)
            throw new ArgumentNullException(nameof(groupPolicies));

        // An empty group should normally be deleted rather than written as an empty payload.
        if (groupPolicies.Count == 0)
            throw new ArgumentException("Group policies must not be empty.", nameof(groupPolicies));

        // Build the canonical Redis key for this policy group.
        var redisKey = BuildPolicyKey(serviceName, subjectType, subjectKey);

        // Sort policies deterministically so payload content and computed version remain stable
        // across runs when the logical data has not changed.
        var orderedPolicies = groupPolicies
            .OrderByDescending(p => p.Priority)
            .ThenBy(p => p.HttpMethodType)
            .ThenBy(p => p.EndpointPattern)
            .ThenBy(p => p.WindowUnit)
            .ThenBy(p => p.WindowSize)
            .ThenBy(p => p.AlgorithmType)
            .ThenBy(p => p.LimitCount)
            .ThenBy(p => p.Id)
            .ToList();

        // Compute a stable version/fingerprint for the group based on its identity and rules.
        var version = ComputeVersion(
            orderedPolicies,
            serviceName,
            subjectType,
            subjectKey);

        // Project database entities into a cache-focused payload model for Redis storage.
        var payload = new CachedRateLimitPolicyGroup
        {
            SchemaVersion = CurrentSchemaVersion,
            Version = version,
            ServiceName = serviceName,
            SubjectType = subjectType,
            SubjectKey = subjectKey,
            SyncedAtUtc = syncedAtUtc,
            Rules = orderedPolicies.Select(p => new CachedRateLimitRule
            {
                Id = p.Id,
                PolicyName = p.PolicyName,
                HttpMethodType = p.HttpMethodType,
                EndpointPattern = p.EndpointPattern,
                LimitCount = p.LimitCount,
                WindowSize = p.WindowSize,
                WindowUnit = p.WindowUnit,
                AlgorithmType = p.AlgorithmType,
                Priority = p.Priority,
                EffectiveFrom = p.EffectiveFrom,
                EffectiveTo = p.EffectiveTo,
                Notes = p.Notes
            }).ToList()
        };

        // Serialize once so the exact payload written to Redis is explicit and easier to inspect.
        var json = JsonSerializer.Serialize(payload, _jsonOptions);

        // Write the policy group payload under its dedicated Redis key.
        await redisDb.StringSetAsync(redisKey, json);

        // Add the key to the central index set so future snapshots and cleanup operations
        // can discover all cached policy group keys without scanning Redis.
        await redisDb.SetAddAsync(PolicyIndexKey, redisKey);
    }

    /// <summary>
    /// Executes a policy sync operation, records its health status and metrics in Redis,
    /// and logs the final outcome for observability.
    /// </summary>
    private async Task ExecuteWithMetricsAsync(IDatabase redisDb, string mode, Func<CancellationToken, Task<PolicySyncMetrics>> action, CancellationToken ct)
    {
        // Capture the start time so both success and failure metrics can report duration.
        var started = DateTime.UtcNow;

        try
        {
            // Execute the actual sync operation and collect the metrics produced by it.
            var metrics = await action(ct);

            // Persist the latest successful sync status and detailed metrics to Redis.
            await WriteHealthAsync(redisDb, new
            {
                status = metrics.Status,
                mode,
                schemaVersion = CurrentSchemaVersion,
                lastSuccessUtc = metrics.FinishedAtUtc,
                durationMs = metrics.DurationMs,
                metrics
            });

            // Write a structured success log so sync behavior can be monitored and queried.
            _logger.LogInformation(
                "Policy sync completed. Mode={Mode}, Status={Status}, DurationMs={DurationMs}, ChangedRows={ChangedRows}, ImpactedGroups={ImpactedGroups}, WrittenGroups={WrittenGroups}, UpdatedGroups={UpdatedGroups}, DeletedGroups={DeletedGroups}, Watermark={Watermark}",
                metrics.Mode,
                metrics.Status,
                metrics.DurationMs,
                metrics.ChangedRows,
                metrics.ImpactedGroups,
                metrics.WrittenGroups,
                metrics.UpdatedGroups,
                metrics.DeletedGroups,
                metrics.Watermark);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Do not record cooperative cancellation as an error; let the caller handle shutdown/cancellation.
            throw;
        }
        catch (Exception ex)
        {
            // Capture the failure time so error metrics can include an accurate duration.
            var finished = DateTime.UtcNow;

            // Build a failure metrics object because the sync action did not complete successfully.
            var metrics = new PolicySyncMetrics
            {
                Mode = mode,
                Status = SyncStatusError,
                SchemaVersion = CurrentSchemaVersion,
                StartedAtUtc = started,
                FinishedAtUtc = finished,
                DurationMs = (finished - started).TotalMilliseconds,
                Error = ex.Message
            };

            _logger.LogError(ex, "Policy sync failed. Mode={Mode}, DurationMs={DurationMs}", mode, metrics.DurationMs);

            // Persist the latest error status to Redis so monitoring can detect sync failures.
            await WriteHealthAsync(redisDb, new
            {
                status = SyncStatusError,
                mode,
                schemaVersion = CurrentSchemaVersion,
                lastErrorUtc = finished,
                lastError = ex.Message,
                metrics
            });

            // Re-throw the original exception while preserving its stack trace.
            throw;
        }
    }

    /// <summary>
    /// Stores the latest policy sync metrics in Redis so the current sync version
    /// and execution details can be inspected later.
    /// </summary>
    private async Task WriteVersionAsync(IDatabase redisDb, PolicySyncMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);


        // Store the latest sync metrics as a JSON payload under the version key.
        await redisDb.StringSetAsync(
            PolicyVersionKey,
            JsonSerializer.Serialize(metrics, _jsonOptions));
    }

    /// <summary>
    /// Stores the latest policy sync health payload in Redis.
    /// </summary>
    private async Task WriteHealthAsync(IDatabase redisDb, object payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        // Store the latest policy sync health payload as a JSON document.
        await redisDb.StringSetAsync(PolicyHealthKey, JsonSerializer.Serialize(payload, _jsonOptions));
    }

    /// <summary>
    /// Creates a metrics payload representing a sync operation that was intentionally skipped.
    /// </summary>
    /// <returns>A metrics object with status set to SKIPPED.</returns>
    private static PolicySyncMetrics CreateSkippedMetrics(string mode, string reason)
    {
        // Use a single UTC timestamp because a skipped sync has no real execution duration.
        var now = DateTime.UtcNow;

        // Return a standardized metrics object so skipped executions are observable like normal runs.
        return new PolicySyncMetrics
        {
            Mode = mode,
            Status = SyncStatusSkipped,
            SchemaVersion = CurrentSchemaVersion,
            StartedAtUtc = now,
            FinishedAtUtc = now,
            DurationMs = 0,
            Error = reason
        };
    }


    /// <summary>
    /// Builds the canonical Redis key for a cached policy group.
    /// </summary>
    /// <returns>The Redis key used to store the policy group.</returns>
    private static string BuildPolicyKey(string serviceName, SubjectType subjectType, string subjectKey)
    {
        // Compose a deterministic Redis key from the policy group identity.
        return $"rl:policy:{serviceName}:{(byte)subjectType}:{subjectKey}";
    }


    /// <summary>
    /// Computes a deterministic version hash for a policy group based on its identity and rule content.
    /// </summary>
    /// <returns>A SHA-256 hex string representing the current content version of the group.</returns>
    private static string ComputeVersion(IEnumerable<RateLimitPolicy> policies, string serviceName, SubjectType subjectType, string subjectKey)
    {
        var sb = new StringBuilder();

        // Include the group identity so the version is scoped to this exact Redis policy key.
        sb.Append(serviceName).Append('|')
          .Append((byte)subjectType).Append('|')
          .Append(subjectKey).Append('|');

        // Canonically order policies before hashing so logically identical groups always produce the same version.
        foreach (var p in policies
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.HttpMethodType)
            .ThenBy(x => x.EndpointPattern)
            .ThenBy(x => x.WindowUnit)
            .ThenBy(x => x.WindowSize)
            .ThenBy(x => x.AlgorithmType)
            .ThenBy(x => x.LimitCount)
            .ThenBy(x => x.Id))
        {
            // Append all rule fields that should affect the cache version/fingerprint.
            sb.Append(p.Id).Append('|')
              .Append(p.PolicyName).Append('|')
              .Append((byte)p.HttpMethodType).Append('|')
              .Append(p.EndpointPattern).Append('|')
              .Append(p.LimitCount).Append('|')
              .Append(p.WindowSize).Append('|')
              .Append((byte)p.WindowUnit).Append('|')
              .Append((byte)p.AlgorithmType).Append('|')
              .Append(p.Priority).Append('|')
              .Append(p.EffectiveFrom?.ToString("O") ?? string.Empty).Append('|')
              .Append(p.EffectiveTo?.ToString("O") ?? string.Empty).Append('|')
              .Append(p.Notes ?? string.Empty)
              .Append(';');
        }

        // Hash the canonical string to produce a compact, fixed-size version token.
        using var sha = SHA256.Create();

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var hash = sha.ComputeHash(bytes);

        // Return the hash as an uppercase hexadecimal string for storage and comparison.
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Represents the logical identity of a cached policy group.
    /// </summary>
    private sealed record GroupKey(string ServiceName, SubjectType SubjectType, string SubjectKey);
}