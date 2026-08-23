using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Neftyanik.Portal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAppliedMemberNightRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AppliedMemberNightRate",
                table: "MemberElectricityReadings",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_MemberElectricityReadings_AppliedMemberNightRate_NonNegative",
                table: "MemberElectricityReadings",
                sql: "[AppliedMemberNightRate] IS NULL OR [AppliedMemberNightRate] >= 0");

            migrationBuilder.Sql(
                @"UPDATE reading
SET reading.[AppliedMemberNightRate] = tariff.[NightRate]
FROM [MemberElectricityReadings] AS reading
INNER JOIN [MemberElectricityMeters] AS meter ON reading.[MemberElectricityMeterId] = meter.[Id]
INNER JOIN [Members] AS member ON meter.[MemberId] = member.[Id]
OUTER APPLY (
    SELECT TOP(1) candidate.[NightRate]
    FROM [MemberElectricityTariffs] AS candidate
    WHERE candidate.[EffectiveFrom] <= reading.[ReadingDate]
    ORDER BY candidate.[EffectiveFrom] DESC, candidate.[Id] DESC
) AS tariff
WHERE reading.[AppliedMemberNightRate] IS NULL
  AND reading.[IsInitialReading] = 0
  AND reading.[CurrentNightReading] IS NOT NULL
  AND member.[ElectricityMeterType] = 1
  AND tariff.[NightRate] IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MemberElectricityReadings_AppliedMemberNightRate_NonNegative",
                table: "MemberElectricityReadings");

            migrationBuilder.DropColumn(
                name: "AppliedMemberNightRate",
                table: "MemberElectricityReadings");
        }
    }
}
