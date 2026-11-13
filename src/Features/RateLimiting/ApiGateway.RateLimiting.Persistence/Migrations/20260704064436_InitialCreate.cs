using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiGateway.RateLimiting.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateTable(
                name: "RateLimitPolicies",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PolicyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ServiceName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    HttpMethodType = table.Column<byte>(type: "tinyint", nullable: false),
                    EndpointPattern = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SubjectType = table.Column<byte>(type: "tinyint", nullable: false),
                    SubjectKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LimitCount = table.Column<int>(type: "int", nullable: false),
                    WindowSize = table.Column<int>(type: "int", nullable: false),
                    WindowUnit = table.Column<byte>(type: "tinyint", nullable: false),
                    AlgorithmType = table.Column<byte>(type: "tinyint", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    EffectiveTo = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "sysutcdatetime()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "sysutcdatetime()"),
                    UpdatedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RateLimitPolicies", x => x.Id);
                    table.CheckConstraint("CK_RateLimitPolicies_AlgorithmType", "[AlgorithmType] IN (1, 2)");
                    table.CheckConstraint("CK_RateLimitPolicies_EffectiveRange", "[EffectiveFrom] IS NULL OR [EffectiveTo] IS NULL OR [EffectiveTo] > [EffectiveFrom]");
                    table.CheckConstraint("CK_RateLimitPolicies_EndpointPattern_NotEmpty", "LEN(LTRIM(RTRIM([EndpointPattern]))) > 0");
                    table.CheckConstraint("CK_RateLimitPolicies_EndpointPattern_Trimmed", "[EndpointPattern] = LTRIM(RTRIM([EndpointPattern]))");
                    table.CheckConstraint("CK_RateLimitPolicies_HttpMethodType", "[HttpMethodType] IN (0, 1, 2, 3, 4, 5, 6, 7)");
                    table.CheckConstraint("CK_RateLimitPolicies_LimitCount", "[LimitCount] > 0");
                    table.CheckConstraint("CK_RateLimitPolicies_PolicyName_NotEmpty", "LEN(LTRIM(RTRIM([PolicyName]))) > 0");
                    table.CheckConstraint("CK_RateLimitPolicies_ServiceName_NotEmpty", "LEN(LTRIM(RTRIM([ServiceName]))) > 0");
                    table.CheckConstraint("CK_RateLimitPolicies_ServiceName_Trimmed", "[ServiceName] = LTRIM(RTRIM([ServiceName]))");
                    table.CheckConstraint("CK_RateLimitPolicies_SubjectKey_BySubjectType", "([SubjectType] = 0 AND LTRIM(RTRIM([SubjectKey])) = N'*') OR ([SubjectType] IN (1, 2, 3) AND LEN(LTRIM(RTRIM([SubjectKey]))) > 0 AND LTRIM(RTRIM([SubjectKey])) <> N'*')");
                    table.CheckConstraint("CK_RateLimitPolicies_SubjectKey_Trimmed", "[SubjectKey] = LTRIM(RTRIM([SubjectKey]))");
                    table.CheckConstraint("CK_RateLimitPolicies_SubjectType", "[SubjectType] IN (0, 1, 2, 3)");
                    table.CheckConstraint("CK_RateLimitPolicies_WindowSize", "[WindowSize] > 0");
                    table.CheckConstraint("CK_RateLimitPolicies_WindowUnit", "[WindowUnit] IN (1, 2, 3, 4, 5)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_RateLimitPolicies_ActiveLookup",
                schema: "dbo",
                table: "RateLimitPolicies",
                columns: new[] { "IsDeleted", "IsActive", "ServiceName", "SubjectType", "SubjectKey" });

            migrationBuilder.CreateIndex(
                name: "IX_RateLimitPolicies_GroupLookup",
                schema: "dbo",
                table: "RateLimitPolicies",
                columns: new[] { "ServiceName", "SubjectType", "SubjectKey" },
                filter: "[IsActive] = 1 AND [IsDeleted] = 0")
                .Annotation("SqlServer:Include", new[] { "Id", "PolicyName", "HttpMethodType", "EndpointPattern", "LimitCount", "WindowSize", "WindowUnit", "AlgorithmType", "Priority", "EffectiveFrom", "EffectiveTo", "IsActive", "IsDeleted", "Notes", "UpdatedAt", "RowVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_RateLimitPolicies_RowVersion",
                schema: "dbo",
                table: "RateLimitPolicies",
                column: "RowVersion");

            migrationBuilder.CreateIndex(
                name: "IX_RateLimitPolicies_UpdatedAt",
                schema: "dbo",
                table: "RateLimitPolicies",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "UX_RateLimitPolicies_ActivePolicy",
                schema: "dbo",
                table: "RateLimitPolicies",
                columns: new[] { "ServiceName", "HttpMethodType", "EndpointPattern", "SubjectType", "SubjectKey", "WindowUnit", "WindowSize" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.Sql("""
CREATE TRIGGER [dbo].[TR_RateLimitPolicies_PreventGroupIdentityUpdate]
ON [dbo].[RateLimitPolicies]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        INNER JOIN deleted d ON i.Id = d.Id
        WHERE
            ISNULL(i.ServiceName, '') <> ISNULL(d.ServiceName, '')
            OR i.SubjectType <> d.SubjectType
            OR ISNULL(i.SubjectKey, '') <> ISNULL(d.SubjectKey, '')
    )
    BEGIN
        THROW 51000, 'ServiceName, SubjectType and SubjectKey are immutable. Deactivate/delete old policy and insert a new one.', 1;
    END
END;
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
DROP TRIGGER IF EXISTS [dbo].[TR_RateLimitPolicies_PreventGroupIdentityUpdate];
""");

            migrationBuilder.DropTable(
                name: "RateLimitPolicies",
                schema: "dbo");
        }
    }
}
