using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Neftyanik.Portal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleAssociationAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT ApplicationUserId FROM (
                        SELECT ApplicationUserId, AssociationId FROM AssociationUserMemberships
                        UNION
                        SELECT ApplicationUserId, AssociationId FROM Members WHERE ApplicationUserId IS NOT NULL
                    ) links GROUP BY ApplicationUserId HAVING COUNT(DISTINCT AssociationId) > 1
                )
                    THROW 51020, 'Account belongs to multiple associations. Explicit operator resolution is required before migration.', 1;
                IF EXISTS (
                    SELECT 1 FROM AspNetUserRoles ur JOIN AspNetRoles r ON r.Id = ur.RoleId
                    WHERE r.NormalizedName = 'PLATFORMADMINISTRATOR' AND (
                        EXISTS (SELECT 1 FROM AssociationUserMemberships m WHERE m.ApplicationUserId = ur.UserId)
                        OR EXISTS (SELECT 1 FROM Members m WHERE m.ApplicationUserId = ur.UserId)
                    )
                )
                    THROW 51021, 'PlatformAdministrator has tenant links. Explicit operator resolution is required before migration.', 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_Members_ApplicationUserId",
                table: "Members");

            migrationBuilder.DropIndex(
                name: "IX_AssociationUserMemberships_ApplicationUserId",
                table: "AssociationUserMemberships");

            migrationBuilder.CreateTable(
                name: "AssociationAccountBindings",
                columns: table => new
                {
                    ApplicationUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    AssociationId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssociationAccountBindings", x => x.ApplicationUserId);
                    table.UniqueConstraint("AK_AssociationAccountBindings_ApplicationUserId_AssociationId", x => new { x.ApplicationUserId, x.AssociationId });
                    table.ForeignKey(
                        name: "FK_AssociationAccountBindings_AspNetUsers_ApplicationUserId",
                        column: x => x.ApplicationUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssociationAccountBindings_Associations_AssociationId",
                        column: x => x.AssociationId,
                        principalTable: "Associations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Members_ApplicationUserId_AssociationId",
                table: "Members",
                columns: new[] { "ApplicationUserId", "AssociationId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssociationUserMemberships_ApplicationUserId_AssociationId",
                table: "AssociationUserMemberships",
                columns: new[] { "ApplicationUserId", "AssociationId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssociationAccountBindings_AssociationId",
                table: "AssociationAccountBindings",
                column: "AssociationId");

            migrationBuilder.Sql("""
                INSERT INTO AssociationAccountBindings (ApplicationUserId, AssociationId)
                SELECT ApplicationUserId, AssociationId FROM AssociationUserMemberships
                UNION
                SELECT ApplicationUserId, AssociationId FROM Members WHERE ApplicationUserId IS NOT NULL;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_AssociationUserMemberships_AssociationAccountBindings_ApplicationUserId_AssociationId",
                table: "AssociationUserMemberships",
                columns: new[] { "ApplicationUserId", "AssociationId" },
                principalTable: "AssociationAccountBindings",
                principalColumns: new[] { "ApplicationUserId", "AssociationId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Members_AssociationAccountBindings_ApplicationUserId_AssociationId",
                table: "Members",
                columns: new[] { "ApplicationUserId", "AssociationId" },
                principalTable: "AssociationAccountBindings",
                principalColumns: new[] { "ApplicationUserId", "AssociationId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AssociationUserMemberships_AssociationAccountBindings_ApplicationUserId_AssociationId",
                table: "AssociationUserMemberships");

            migrationBuilder.DropForeignKey(
                name: "FK_Members_AssociationAccountBindings_ApplicationUserId_AssociationId",
                table: "Members");

            migrationBuilder.DropTable(
                name: "AssociationAccountBindings");

            migrationBuilder.DropIndex(
                name: "IX_Members_ApplicationUserId_AssociationId",
                table: "Members");

            migrationBuilder.DropIndex(
                name: "IX_AssociationUserMemberships_ApplicationUserId_AssociationId",
                table: "AssociationUserMemberships");

            migrationBuilder.CreateIndex(
                name: "IX_Members_ApplicationUserId",
                table: "Members",
                column: "ApplicationUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssociationUserMemberships_ApplicationUserId",
                table: "AssociationUserMemberships",
                column: "ApplicationUserId");
        }
    }
}
