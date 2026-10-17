using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Neftyanik.Portal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesRequestNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NotificationAttemptCount",
                table: "DemoRequests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "NotificationNextAttemptAtUtc",
                table: "DemoRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NotificationSentAtUtc",
                table: "DemoRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DemoRequests_NotificationSentAtUtc_NotificationNextAttemptAtUtc",
                table: "DemoRequests",
                columns: new[] { "NotificationSentAtUtc", "NotificationNextAttemptAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DemoRequests_NotificationSentAtUtc_NotificationNextAttemptAtUtc",
                table: "DemoRequests");

            migrationBuilder.DropColumn(
                name: "NotificationAttemptCount",
                table: "DemoRequests");

            migrationBuilder.DropColumn(
                name: "NotificationNextAttemptAtUtc",
                table: "DemoRequests");

            migrationBuilder.DropColumn(
                name: "NotificationSentAtUtc",
                table: "DemoRequests");
        }
    }
}
