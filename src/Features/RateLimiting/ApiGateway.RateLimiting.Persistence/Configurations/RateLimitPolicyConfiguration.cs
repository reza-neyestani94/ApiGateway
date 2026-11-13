using ApiGateway.RateLimiting.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApiGateway.RateLimiting.Persistence.Configurations;

public sealed class RateLimitPolicyConfiguration : IEntityTypeConfiguration<RateLimitPolicy>
{
    public void Configure(EntityTypeBuilder<RateLimitPolicy> builder)
    {
        builder.ToTable("RateLimitPolicies", "dbo", table =>
        {
            table.HasTrigger("TR_RateLimitPolicies_PreventGroupIdentityUpdate");
        });

        builder.HasKey(x => x.Id)
            .HasName("PK_RateLimitPolicies");

        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.PolicyName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.ServiceName)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(x => x.HttpMethodType)
            .HasConversion<byte>()
            .IsRequired();

        builder.Property(x => x.EndpointPattern)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.SubjectType)
            .HasConversion<byte>()
            .IsRequired();

        builder.Property(x => x.SubjectKey)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.LimitCount)
            .IsRequired();

        builder.Property(x => x.WindowSize)
            .IsRequired();

        builder.Property(x => x.WindowUnit)
            .HasConversion<byte>()
            .IsRequired();

        builder.Property(x => x.AlgorithmType)
            .HasConversion<byte>()
            .IsRequired();

        builder.Property(x => x.Priority)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(x => x.EffectiveFrom)
            .HasColumnType("datetime2(3)");

        builder.Property(x => x.EffectiveTo)
            .HasColumnType("datetime2(3)");

        builder.Property(x => x.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(x => x.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.Notes)
            .HasMaxLength(1000);

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasColumnType("datetime2(3)")
            .HasDefaultValueSql("sysutcdatetime()");

        builder.Property(x => x.UpdatedAt)
            .IsRequired()
            .HasColumnType("datetime2(3)")
            .HasDefaultValueSql("sysutcdatetime()");

        builder.Property(x => x.UpdatedBy)
            .HasMaxLength(150);

        builder.Property(x => x.RowVersion)
            .IsRequired()
            .IsRowVersion()
            .IsConcurrencyToken();

        builder.HasIndex(x => new
        {
            x.ServiceName,
            x.SubjectType,
            x.SubjectKey
        })
            .HasDatabaseName("IX_RateLimitPolicies_ActiveGroupLookup")
            .HasFilter("[IsActive] = 1 AND [IsDeleted] = 0")
            .IncludeProperties(x => new
            {
                x.Id,
                x.PolicyName,
                x.HttpMethodType,
                x.EndpointPattern,
                x.LimitCount,
                x.WindowSize,
                x.WindowUnit,
                x.AlgorithmType,
                x.Priority,
                x.EffectiveFrom,
                x.EffectiveTo,
                x.Notes,
                x.RowVersion
            });

        builder.HasIndex(x => new
        {
            x.IsDeleted,
            x.IsActive,
            x.ServiceName,
            x.SubjectType,
            x.SubjectKey
        })
            .HasDatabaseName("IX_RateLimitPolicies_ActiveLookup");

        builder.HasIndex(x => new
        {
            x.ServiceName,
            x.SubjectType,
            x.SubjectKey
        })
            .HasDatabaseName("IX_RateLimitPolicies_GroupLookup")
            .IncludeProperties(x => new
            {
                x.Id,
                x.PolicyName,
                x.HttpMethodType,
                x.EndpointPattern,
                x.LimitCount,
                x.WindowSize,
                x.WindowUnit,
                x.AlgorithmType,
                x.Priority,
                x.EffectiveFrom,
                x.EffectiveTo,
                x.IsActive,
                x.IsDeleted,
                x.Notes,
                x.UpdatedAt,
                x.RowVersion
            });

        builder.HasIndex(x => x.RowVersion)
            .HasDatabaseName("IX_RateLimitPolicies_RowVersion");

        builder.HasIndex(x => x.UpdatedAt)
            .HasDatabaseName("IX_RateLimitPolicies_UpdatedAt");

        builder.HasIndex(x => new
        {
            x.ServiceName,
            x.HttpMethodType,
            x.EndpointPattern,
            x.SubjectType,
            x.SubjectKey,
            x.WindowUnit,
            x.WindowSize
        })
            .IsUnique()
            .HasDatabaseName("UX_RateLimitPolicies_ActivePolicy")
            .HasFilter("[IsDeleted] = 0");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_RateLimitPolicies_AlgorithmType",
                "[AlgorithmType] IN (1, 2)");

            table.HasCheckConstraint(
                "CK_RateLimitPolicies_EffectiveRange",
                "[EffectiveFrom] IS NULL OR [EffectiveTo] IS NULL OR [EffectiveTo] > [EffectiveFrom]");

            table.HasCheckConstraint(
                "CK_RateLimitPolicies_EndpointPattern_NotEmpty",
                "LEN(LTRIM(RTRIM([EndpointPattern]))) > 0");

            table.HasCheckConstraint(
                "CK_RateLimitPolicies_EndpointPattern_Trimmed",
                "[EndpointPattern] = LTRIM(RTRIM([EndpointPattern]))");

            table.HasCheckConstraint(
                "CK_RateLimitPolicies_HttpMethodType",
                "[HttpMethodType] IN (0, 1, 2, 3, 4, 5, 6, 7)");

            table.HasCheckConstraint(
                "CK_RateLimitPolicies_LimitCount",
                "[LimitCount] > 0");

            table.HasCheckConstraint(
                "CK_RateLimitPolicies_PolicyName_NotEmpty",
                "LEN(LTRIM(RTRIM([PolicyName]))) > 0");

            table.HasCheckConstraint(
                "CK_RateLimitPolicies_ServiceName_NotEmpty",
                "LEN(LTRIM(RTRIM([ServiceName]))) > 0");

            table.HasCheckConstraint(
                "CK_RateLimitPolicies_ServiceName_Trimmed",
                "[ServiceName] = LTRIM(RTRIM([ServiceName]))");

            table.HasCheckConstraint(
                "CK_RateLimitPolicies_SubjectKey_BySubjectType",
                "([SubjectType] = 0 AND LTRIM(RTRIM([SubjectKey])) = N'*') OR ([SubjectType] IN (1, 2, 3) AND LEN(LTRIM(RTRIM([SubjectKey]))) > 0 AND LTRIM(RTRIM([SubjectKey])) <> N'*')");

            table.HasCheckConstraint(
                "CK_RateLimitPolicies_SubjectKey_Trimmed",
                "[SubjectKey] = LTRIM(RTRIM([SubjectKey]))");

            table.HasCheckConstraint(
                "CK_RateLimitPolicies_SubjectType",
                "[SubjectType] IN (0, 1, 2, 3)");

            table.HasCheckConstraint(
                "CK_RateLimitPolicies_WindowSize",
                "[WindowSize] > 0");

            table.HasCheckConstraint(
                "CK_RateLimitPolicies_WindowUnit",
                "[WindowUnit] IN (1, 2, 3, 4, 5)");
        });
    }
}