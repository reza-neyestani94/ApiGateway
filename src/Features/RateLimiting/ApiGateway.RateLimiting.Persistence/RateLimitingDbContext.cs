using ApiGateway.RateLimiting.Persistence.Configurations;
using ApiGateway.RateLimiting.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ApiGateway.RateLimiting.Persistence;

public class RateLimitingDbContext : DbContext
{
    public RateLimitingDbContext(DbContextOptions<RateLimitingDbContext> options) : base(options) { }

    public DbSet<RateLimitPolicy> RateLimitPolicies => Set<RateLimitPolicy>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new RateLimitPolicyConfiguration());
    }
}
