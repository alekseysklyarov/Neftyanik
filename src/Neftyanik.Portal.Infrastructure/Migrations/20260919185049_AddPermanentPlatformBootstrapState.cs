using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Neftyanik.Portal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPermanentPlatformBootstrapState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource = N'DachaHub.FirstPlatformAdministrator',
                    @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 10000;
                IF @result < 0 THROW 51000, 'Cannot acquire platform bootstrap migration lock.', 1;
                """);
            migrationBuilder.CreateTable(
                name: "PlatformBootstrapStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Disposition = table.Column<int>(type: "int", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OperatorIdentity = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ApprovalReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InitializedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    InitializedUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformBootstrapStates", x => x.Id);
                    table.CheckConstraint("CK_PlatformBootstrapStates_Singleton", "[Id] = 1");
                });
            // A review-required marker still blocks ordinary bootstrap. No absence check proves history.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [AspNetRoles] WHERE [NormalizedName] = N'PLATFORMADMINISTRATOR' OR [Name] = N'PlatformAdministrator')
                    OR EXISTS (SELECT 1 FROM [AspNetRoleClaims] WHERE [ClaimType] = N'dachahub:first-platform-administrator-provisioned' OR [ClaimValue] = N'PlatformAdministrator')
                    OR EXISTS (SELECT 1 FROM [AspNetUserClaims] WHERE [ClaimType] = N'dachahub:first-platform-administrator-provisioned' OR [ClaimValue] = N'PlatformAdministrator')
                    OR EXISTS (SELECT 1 FROM [AspNetUserTokens] WHERE [LoginProvider] = N'DachaHub.PlatformOnboarding')
                BEGIN
                    INSERT INTO [PlatformBootstrapStates] ([Id], [Disposition], [ConsumedAtUtc], [Reason])
                    VALUES (1, 0, SYSUTCDATETIME(), CASE WHEN EXISTS
                        (SELECT 1 FROM [AspNetRoleClaims] WHERE [ClaimType] = N'dachahub:first-platform-administrator-provisioned')
                        THEN N'Legacy bootstrap marker migrated' ELSE N'Platform evidence; bootstrap consumed' END);
                END
                ELSE IF EXISTS (SELECT 1 FROM [AspNetUsers])
                BEGIN
                    INSERT INTO [PlatformBootstrapStates] ([Id], [Disposition], [ConsumedAtUtc], [Reason])
                    VALUES (1, 1, SYSUTCDATETIME(), N'Legacy history requires operator review; bootstrap closed');
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("The permanent platform bootstrap security barrier cannot be removed by rollback.");
        }
    }
}
