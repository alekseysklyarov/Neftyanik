using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Neftyanik.Portal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberNightTariffAndMeterType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ElectricityMeterType",
                table: "Members",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "NightRate",
                table: "MemberElectricityTariffs",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_MemberElectricityTariffs_NightRate_NonNegative",
                table: "MemberElectricityTariffs",
                sql: "[NightRate] IS NULL OR [NightRate] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MemberElectricityTariffs_NightRate_NonNegative",
                table: "MemberElectricityTariffs");

            migrationBuilder.DropColumn(
                name: "ElectricityMeterType",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "NightRate",
                table: "MemberElectricityTariffs");
        }
    }
}
