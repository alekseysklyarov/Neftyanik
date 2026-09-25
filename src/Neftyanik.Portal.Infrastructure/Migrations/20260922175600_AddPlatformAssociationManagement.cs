using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Neftyanik.Portal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformAssociationManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContactEmail",
                table: "Associations",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPhone",
                table: "Associations",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalAddress",
                table: "Associations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Revision",
                table: "Associations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "PlatformAuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssociationId = table.Column<int>(type: "int", nullable: false),
                    OperatorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OldValuesJson = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: false),
                    NewValuesJson = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformAuditLogs_AspNetUsers_OperatorUserId",
                        column: x => x.OperatorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlatformAuditLogs_Associations_AssociationId",
                        column: x => x.AssociationId,
                        principalTable: "Associations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "Associations",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "ContactEmail", "ContactPhone", "PostalAddress", "Revision" },
                values: new object[] { null, null, null, new Guid("00000000-0000-0000-0000-000000000000") });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAuditLogs_AssociationId_OccurredAtUtc",
                table: "PlatformAuditLogs",
                columns: new[] { "AssociationId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAuditLogs_OperatorUserId",
                table: "PlatformAuditLogs",
                column: "OperatorUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformAuditLogs");

            migrationBuilder.DropColumn(
                name: "ContactEmail",
                table: "Associations");

            migrationBuilder.DropColumn(
                name: "ContactPhone",
                table: "Associations");

            migrationBuilder.DropColumn(
                name: "PostalAddress",
                table: "Associations");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "Associations");
        }
    }
}
