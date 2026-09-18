using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Neftyanik.Portal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAssociationMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DECLARE @associationId int = (SELECT [Id] FROM [Associations] WHERE [Slug] = N'neftyanik');
                IF @associationId IS NULL
                    THROW 51030, 'Stage 3: association neftyanik is missing. No memberships were migrated.', 1;

                IF EXISTS (
                    SELECT 1 FROM [AspNetUserRoles] ur JOIN [AspNetRoles] r ON r.[Id] = ur.[RoleId]
                    WHERE r.[Name] COLLATE Latin1_General_100_BIN2 NOT IN (N'Administrator', N'Accountant', N'Member')
                       OR r.[Name] IS NULL)
                    THROW 51031, 'Stage 3: unrecognized assigned Identity role. Review AspNetUserRoles and AspNetRoles before migration.', 1;

                IF EXISTS (SELECT 1 FROM [AspNetUserClaims] WHERE [ClaimType] IN
                    (N'http://schemas.microsoft.com/ws/2008/06/identity/claims/role', N'role', N'dachahub:association-role'))
                    OR EXISTS (SELECT 1 FROM [AspNetRoleClaims] WHERE [ClaimType] IN
                    (N'http://schemas.microsoft.com/ws/2008/06/identity/claims/role', N'role', N'dachahub:association-role'))
                    THROW 51032, 'Stage 3: legacy role claims require explicit review; privileges will not be inferred from claims.', 1;

                IF EXISTS (SELECT 1 FROM [Members] m WHERE m.[ApplicationUserId] IS NOT NULL
                    AND NOT EXISTS (SELECT 1 FROM [AspNetUserRoles] ur WHERE ur.[UserId] = m.[ApplicationUserId]))
                    THROW 51033, 'Stage 3: linked Member has no Identity role. Review Members.ApplicationUserId; no role will be inferred.', 1;

                IF EXISTS (SELECT 1 FROM [Members] m WHERE m.[ApplicationUserId] IS NOT NULL
                    AND m.[AssociationId] <> @associationId)
                    THROW 51034, 'Stage 3: legacy user links exist outside neftyanik. Global roles cannot be attributed automatically.', 1;

                IF EXISTS (SELECT 1 FROM [Members] WHERE [ApplicationUserId] IS NOT NULL
                    GROUP BY [AssociationId], [ApplicationUserId] HAVING COUNT(*) > 1)
                    THROW 51035, 'Stage 3: multiple Member records for one user in an association require review.', 1;
                """);

            migrationBuilder.CreateTable(
                name: "AssociationLoginEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssociationId = table.Column<int>(type: "int", nullable: false),
                    UserLoginHistoryId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssociationLoginEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssociationLoginEvents_Associations_AssociationId",
                        column: x => x.AssociationId,
                        principalTable: "Associations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssociationLoginEvents_UserLoginHistories_UserLoginHistoryId",
                        column: x => x.UserLoginHistoryId,
                        principalTable: "UserLoginHistories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssociationUserMemberships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssociationId = table.Column<int>(type: "int", nullable: false),
                    ApplicationUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssociationUserMemberships", x => x.Id);
                    table.CheckConstraint("CK_AssociationUserMemberships_Role", "[Role] IN ('Administrator', 'Accountant', 'Member')");
                    table.ForeignKey(
                        name: "FK_AssociationUserMemberships_AspNetUsers_ApplicationUserId",
                        column: x => x.ApplicationUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssociationUserMemberships_Associations_AssociationId",
                        column: x => x.AssociationId,
                        principalTable: "Associations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssociationLoginEvents_AssociationId",
                table: "AssociationLoginEvents",
                column: "AssociationId");

            migrationBuilder.CreateIndex(
                name: "IX_AssociationLoginEvents_UserLoginHistoryId",
                table: "AssociationLoginEvents",
                column: "UserLoginHistoryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssociationUserMemberships_ApplicationUserId",
                table: "AssociationUserMemberships",
                column: "ApplicationUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssociationUserMemberships_AssociationId_ApplicationUserId_Role",
                table: "AssociationUserMemberships",
                columns: new[] { "AssociationId", "ApplicationUserId", "Role" },
                unique: true);

            migrationBuilder.Sql("""
                DECLARE @associationId int = (SELECT [Id] FROM [Associations] WHERE [Slug] = N'neftyanik');
                INSERT INTO [AssociationUserMemberships]
                    ([AssociationId], [ApplicationUserId], [Role], [IsActive], [CreatedAtUtc])
                SELECT DISTINCT @associationId, u.[Id], r.[Name], u.[IsActive], TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')
                FROM [AspNetUsers] u
                JOIN [AspNetUserRoles] ur ON ur.[UserId] = u.[Id]
                JOIN [AspNetRoles] r ON r.[Id] = ur.[RoleId]
                WHERE r.[Name] COLLATE Latin1_General_100_BIN2 IN (N'Administrator', N'Accountant', N'Member')
                    AND NOT EXISTS (SELECT 1 FROM [AssociationUserMemberships] m
                        WHERE m.[AssociationId] = @associationId AND m.[ApplicationUserId] = u.[Id] AND m.[Role] = r.[Name]);
                """);
            // Legacy history has no tenant provenance. Preserve it globally without inventing links.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Stage 3 rollback would destroy tenant memberships and login attribution. Restore a reviewed database backup instead.");
        }
    }
}
