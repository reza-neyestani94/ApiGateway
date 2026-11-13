using ApiGateway.RateLimiting.Persistence;
using ApiGateway.RateLimiting.Sync;
using ApiGateway.RateLimiting.Sync.Options;
using Microsoft.EntityFrameworkCore;
using Serilog;
using StackExchange.Redis;
using Microsoft.Extensions.Hosting.WindowsServices;

// تنظیم محیط (Environment)
var environment =
    Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
    ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
    ?? Environments.Production;

try
{
    // تنظیمات اولیه لاگ برای شروع به کار (Bootstrap)
    var bootstrapConfig = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
        .AddEnvironmentVariables()
        .AddCommandLine(args)
        .Build();

    Log.Logger = new LoggerConfiguration()
        .ReadFrom.Configuration(bootstrapConfig)
        .CreateLogger();

    Log.Information(
        "Starting ApiGateway RateLimiting Sync Service. Environment={Environment}, BaseDirectory={BaseDirectory}",
        environment,
        AppContext.BaseDirectory);

    var builder = Host.CreateDefaultBuilder(args)
        .UseEnvironment(environment)
        .UseWindowsService(options =>
        {
            options.ServiceName = "ApiGateway RateLimiting Sync Service";
        })
        .ConfigureAppConfiguration((hostingContext, configBuilder) =>
        {
            configBuilder.Sources.Clear();
            configBuilder
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{hostingContext.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .AddCommandLine(args);
        })
        .UseSerilog((ctx, services, loggerConfiguration) =>
        {
            loggerConfiguration
                .ReadFrom.Configuration(ctx.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext();
        })
        .ConfigureServices((ctx, services) =>
        {
            var cfg = ctx.Configuration;

            // ۱. تنظیمات SQL Server از پروژه Persistence
            var sqlConnectionString = cfg.GetConnectionString("Sql");
            if (string.IsNullOrWhiteSpace(sqlConnectionString))
                throw new InvalidOperationException("Connection string 'Sql' is missing.");

            var sqlLoggingEnabled = cfg.GetSection("Logging").GetValue<bool>("SqlCommands");

            services.AddDbContext<RateLimitingDbContext>(options =>
            {
                options.UseSqlServer(sqlConnectionString);
                if (sqlLoggingEnabled)
                {
                    options.LogTo(Console.WriteLine, LogLevel.Information);
                    options.EnableSensitiveDataLogging();
                    options.EnableDetailedErrors();
                }
            });

            // ۲. تنظیمات Redis Options
            services.AddOptions<RedisOptions>()
                    .Bind(cfg.GetSection("Redis"))
                    .Validate(x => x.LockTtlSeconds > 0, "LockTtlSeconds must be > 0")
                    .Validate(x => !string.IsNullOrWhiteSpace(x.LockKey), "LockKey is required")
                    .ValidateOnStart();

            // ۳. تنظیمات Sync Options
            services.Configure<SyncOptions>(cfg.GetSection("PolicySync"));

            // ۴. تنظیمات Redis Connection
            var redisConnectionString = cfg.GetSection("Redis").GetValue<string>("Connection");
            if (string.IsNullOrWhiteSpace(redisConnectionString))
                throw new InvalidOperationException("Redis:Connection is missing.");

            services.AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(redisConnectionString));

            // ۵. ثبت خودِ سرویس Worker
            // نکته: اگر اسم کلاس را در فایل قبلی به PolicySyncWorker تغییر دادی، اینجا هم عوض کن
            services.AddHostedService<PolicySyncWorker>();
        });

    await builder.Build().RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "RateLimiting Sync Service terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
