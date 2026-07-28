using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Neftyanik.Portal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChargeTypeRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "ChargeTypes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsYearly",
                table: "ChargeTypes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "OnlyOnOwnerChange",
                table: "ChargeTypes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ChargeTypes_IsDefault",
                table: "ChargeTypes",
                column: "IsDefault",
                unique: true,
                filter: "[IsDefault] = 1 AND [IsActive] = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ChargeTypes_YearlyAndOwnerChangeExclusive",
                table: "ChargeTypes",
                sql: "[IsYearly] = 0 OR [OnlyOnOwnerChange] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChargeTypes_IsDefault",
                table: "ChargeTypes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ChargeTypes_YearlyAndOwnerChangeExclusive",
                table: "ChargeTypes");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "ChargeTypes");

            migrationBuilder.DropColumn(
                name: "IsYearly",
                table: "ChargeTypes");

            migrationBuilder.DropColumn(
                name: "OnlyOnOwnerChange",
                table: "ChargeTypes");
        }
    }
}
