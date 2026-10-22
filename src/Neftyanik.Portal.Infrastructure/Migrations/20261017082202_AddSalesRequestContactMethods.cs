using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Neftyanik.Portal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesRequestContactMethods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "DemoRequests",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreferredContactMethod",
                table: "DemoRequests",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "email");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Phone",
                table: "DemoRequests");

            migrationBuilder.DropColumn(
                name: "PreferredContactMethod",
                table: "DemoRequests");
        }
    }
}
