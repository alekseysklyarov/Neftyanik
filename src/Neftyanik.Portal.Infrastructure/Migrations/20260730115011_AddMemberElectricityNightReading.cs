using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Neftyanik.Portal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberElectricityNightReading : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CurrentNightReading",
                table: "MemberElectricityReadings",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_MemberElectricityReadings_CurrentNightReading_NonNegative",
                table: "MemberElectricityReadings",
                sql: "[CurrentNightReading] IS NULL OR [CurrentNightReading] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MemberElectricityReadings_CurrentNightReading_NonNegative",
                table: "MemberElectricityReadings");

            migrationBuilder.DropColumn(
                name: "CurrentNightReading",
                table: "MemberElectricityReadings");
        }
    }
}
