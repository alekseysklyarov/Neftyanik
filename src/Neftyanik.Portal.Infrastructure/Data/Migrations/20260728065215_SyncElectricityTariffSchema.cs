using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Neftyanik.Portal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SyncElectricityTariffSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ElectricityTariffs_EffectiveFrom",
                table: "ElectricityTariffs");

            migrationBuilder.DeleteData(
                table: "ElectricityTariffs",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DropColumn(
                name: "DayRatePrice",
                table: "ElectricityTariffs");

            migrationBuilder.DropColumn(
                name: "EffectiveTo",
                table: "ElectricityTariffs");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "ElectricityTariffs");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "ElectricityTariffs");

            migrationBuilder.DropColumn(
                name: "NightRatePrice",
                table: "ElectricityTariffs");

            migrationBuilder.DropColumn(
                name: "SingleRatePrice",
                table: "ElectricityTariffs");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "ElectricityTariffs",
                newName: "CreatedAtUtc");

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "ElectricityTariffs",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DayRate",
                table: "ElectricityTariffs",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "NightRate",
                table: "ElectricityTariffs",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "ChargeTypes",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ElectricityReadings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlotId = table.Column<int>(type: "int", nullable: false),
                    ReadingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PreviousDayReading = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    CurrentDayReading = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    DayConsumption = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    DayRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    DayAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    PreviousNightReading = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    CurrentNightReading = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    NightConsumption = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    NightRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    NightAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    IsInitialReading = table.Column<bool>(type: "bit", nullable: false),
                    ChargeId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectricityReadings", x => x.Id);
                    table.CheckConstraint("CK_ElectricityReadings_CurrentDayReading_NonNegative", "[CurrentDayReading] >= 0");
                    table.CheckConstraint("CK_ElectricityReadings_CurrentNightReading_NonNegative", "[CurrentNightReading] >= 0");
                    table.CheckConstraint("CK_ElectricityReadings_DayAmount_NonNegative", "[DayAmount] IS NULL OR [DayAmount] >= 0");
                    table.CheckConstraint("CK_ElectricityReadings_DayConsumption_NonNegative", "[DayConsumption] IS NULL OR [DayConsumption] >= 0");
                    table.CheckConstraint("CK_ElectricityReadings_NightAmount_NonNegative", "[NightAmount] IS NULL OR [NightAmount] >= 0");
                    table.CheckConstraint("CK_ElectricityReadings_NightConsumption_NonNegative", "[NightConsumption] IS NULL OR [NightConsumption] >= 0");
                    table.CheckConstraint("CK_ElectricityReadings_PreviousDayReading_NonNegative", "[PreviousDayReading] IS NULL OR [PreviousDayReading] >= 0");
                    table.CheckConstraint("CK_ElectricityReadings_PreviousNightReading_NonNegative", "[PreviousNightReading] IS NULL OR [PreviousNightReading] >= 0");
                    table.CheckConstraint("CK_ElectricityReadings_TotalAmount_NonNegative", "[TotalAmount] IS NULL OR [TotalAmount] >= 0");
                    table.ForeignKey(
                        name: "FK_ElectricityReadings_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ElectricityReadings_Charges_ChargeId",
                        column: x => x.ChargeId,
                        principalTable: "Charges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ElectricityReadings_Plots_PlotId",
                        column: x => x.PlotId,
                        principalTable: "Plots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ElectricityTariffs_CreatedByUserId",
                table: "ElectricityTariffs",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectricityTariffs_EffectiveFrom",
                table: "ElectricityTariffs",
                column: "EffectiveFrom",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ElectricityTariffs_DayRate_NonNegative",
                table: "ElectricityTariffs",
                sql: "[DayRate] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ElectricityTariffs_NightRate_NonNegative",
                table: "ElectricityTariffs",
                sql: "[NightRate] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_ChargeTypes_Code",
                table: "ChargeTypes",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ElectricityReadings_ChargeId",
                table: "ElectricityReadings",
                column: "ChargeId",
                unique: true,
                filter: "[ChargeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ElectricityReadings_CreatedByUserId",
                table: "ElectricityReadings",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectricityReadings_PlotId",
                table: "ElectricityReadings",
                column: "PlotId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectricityReadings_PlotId_ReadingDate",
                table: "ElectricityReadings",
                columns: new[] { "PlotId", "ReadingDate" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ElectricityTariffs_AspNetUsers_CreatedByUserId",
                table: "ElectricityTariffs",
                column: "CreatedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ElectricityTariffs_AspNetUsers_CreatedByUserId",
                table: "ElectricityTariffs");

            migrationBuilder.DropTable(
                name: "ElectricityReadings");

            migrationBuilder.DropIndex(
                name: "IX_ElectricityTariffs_CreatedByUserId",
                table: "ElectricityTariffs");

            migrationBuilder.DropIndex(
                name: "IX_ElectricityTariffs_EffectiveFrom",
                table: "ElectricityTariffs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ElectricityTariffs_DayRate_NonNegative",
                table: "ElectricityTariffs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ElectricityTariffs_NightRate_NonNegative",
                table: "ElectricityTariffs");

            migrationBuilder.DropIndex(
                name: "IX_ChargeTypes_Code",
                table: "ChargeTypes");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "ElectricityTariffs");

            migrationBuilder.DropColumn(
                name: "DayRate",
                table: "ElectricityTariffs");

            migrationBuilder.DropColumn(
                name: "NightRate",
                table: "ElectricityTariffs");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "ChargeTypes");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "ElectricityTariffs",
                newName: "CreatedAt");

            migrationBuilder.AddColumn<decimal>(
                name: "DayRatePrice",
                table: "ElectricityTariffs",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EffectiveTo",
                table: "ElectricityTariffs",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "ElectricityTariffs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "ElectricityTariffs",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "NightRatePrice",
                table: "ElectricityTariffs",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SingleRatePrice",
                table: "ElectricityTariffs",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.InsertData(
                table: "ElectricityTariffs",
                columns: new[] { "Id", "CreatedAt", "DayRatePrice", "EffectiveFrom", "EffectiveTo", "IsActive", "Name", "NightRatePrice", "SingleRatePrice" },
                values: new object[] { 1, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, new DateOnly(2026, 1, 1), null, true, "Тариф 5,00 грн/кВт·ч", null, 5.00m });

            migrationBuilder.CreateIndex(
                name: "IX_ElectricityTariffs_EffectiveFrom",
                table: "ElectricityTariffs",
                column: "EffectiveFrom");
        }
    }
}
