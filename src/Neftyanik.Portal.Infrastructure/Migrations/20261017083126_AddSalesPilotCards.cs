using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Neftyanik.Portal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesPilotCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NextAction",
                table: "DemoRequests",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "NextActionDate",
                table: "DemoRequests",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PilotEndDate",
                table: "DemoRequests",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PilotNotes",
                table: "DemoRequests",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PilotRevision",
                table: "DemoRequests",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "PilotStage",
                table: "DemoRequests",
                type: "nvarchar(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "new");

            migrationBuilder.AddColumn<DateOnly>(
                name: "PilotStartDate",
                table: "DemoRequests",
                type: "date",
                nullable: true);
            migrationBuilder.Sql("UPDATE [DemoRequests] SET [PilotStage] = 'contacted' WHERE [ContactedAtUtc] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NextAction",
                table: "DemoRequests");

            migrationBuilder.DropColumn(
                name: "NextActionDate",
                table: "DemoRequests");

            migrationBuilder.DropColumn(
                name: "PilotEndDate",
                table: "DemoRequests");

            migrationBuilder.DropColumn(
                name: "PilotNotes",
                table: "DemoRequests");

            migrationBuilder.DropColumn(
                name: "PilotRevision",
                table: "DemoRequests");

            migrationBuilder.DropColumn(
                name: "PilotStage",
                table: "DemoRequests");

            migrationBuilder.DropColumn(
                name: "PilotStartDate",
                table: "DemoRequests");
        }
    }
}
