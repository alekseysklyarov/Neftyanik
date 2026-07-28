using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Neftyanik.Portal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleOpenOwnershipPerPlot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlotOwnerships_PlotId_MemberId",
                table: "PlotOwnerships");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Charges_Amount_Positive",
                table: "Charges");

            migrationBuilder.CreateTable(
                name: "AssociationElectricityReadings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReadingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PreviousDayReading = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    CurrentDayReading = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    DayConsumption = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    AppliedSupplierDayRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    DayAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    PreviousNightReading = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    CurrentNightReading = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    NightConsumption = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    AppliedSupplierNightRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    NightAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    TotalConsumption = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    TotalSupplierAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    IsInitialReading = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssociationElectricityReadings", x => x.Id);
                    table.CheckConstraint("CK_AssociationElectricityReadings_CurrentDayReading_NonNegative", "[CurrentDayReading] >= 0");
                    table.CheckConstraint("CK_AssociationElectricityReadings_CurrentNightReading_NonNegative", "[CurrentNightReading] >= 0");
                    table.CheckConstraint("CK_AssociationElectricityReadings_DayAmount_NonNegative", "[DayAmount] IS NULL OR [DayAmount] >= 0");
                    table.CheckConstraint("CK_AssociationElectricityReadings_DayConsumption_NonNegative", "[DayConsumption] IS NULL OR [DayConsumption] >= 0");
                    table.CheckConstraint("CK_AssociationElectricityReadings_NightAmount_NonNegative", "[NightAmount] IS NULL OR [NightAmount] >= 0");
                    table.CheckConstraint("CK_AssociationElectricityReadings_NightConsumption_NonNegative", "[NightConsumption] IS NULL OR [NightConsumption] >= 0");
                    table.CheckConstraint("CK_AssociationElectricityReadings_PreviousDayReading_NonNegative", "[PreviousDayReading] IS NULL OR [PreviousDayReading] >= 0");
                    table.CheckConstraint("CK_AssociationElectricityReadings_PreviousNightReading_NonNegative", "[PreviousNightReading] IS NULL OR [PreviousNightReading] >= 0");
                    table.CheckConstraint("CK_AssociationElectricityReadings_TotalConsumption_NonNegative", "[TotalConsumption] IS NULL OR [TotalConsumption] >= 0");
                    table.CheckConstraint("CK_AssociationElectricityReadings_TotalSupplierAmount_NonNegative", "[TotalSupplierAmount] IS NULL OR [TotalSupplierAmount] >= 0");
                    table.ForeignKey(
                        name: "FK_AssociationElectricityReadings_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssociationElectricityTariffs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    DayRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    NightRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssociationElectricityTariffs", x => x.Id);
                    table.CheckConstraint("CK_AssociationElectricityTariffs_DayRate_NonNegative", "[DayRate] >= 0");
                    table.CheckConstraint("CK_AssociationElectricityTariffs_NightRate_NonNegative", "[NightRate] >= 0");
                    table.ForeignKey(
                        name: "FK_AssociationElectricityTariffs_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MemberElectricityMeters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MemberId = table.Column<int>(type: "int", nullable: false),
                    MeterNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    BillingPlotId = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberElectricityMeters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MemberElectricityMeters_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MemberElectricityMeters_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MemberElectricityMeters_Plots_BillingPlotId",
                        column: x => x.BillingPlotId,
                        principalTable: "Plots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MemberElectricityTariffs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberElectricityTariffs", x => x.Id);
                    table.CheckConstraint("CK_MemberElectricityTariffs_Rate_NonNegative", "[Rate] >= 0");
                    table.ForeignKey(
                        name: "FK_MemberElectricityTariffs_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MemberElectricityMeterPlots",
                columns: table => new
                {
                    MemberElectricityMeterId = table.Column<int>(type: "int", nullable: false),
                    PlotId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberElectricityMeterPlots", x => new { x.MemberElectricityMeterId, x.PlotId });
                    table.ForeignKey(
                        name: "FK_MemberElectricityMeterPlots_MemberElectricityMeters_MemberElectricityMeterId",
                        column: x => x.MemberElectricityMeterId,
                        principalTable: "MemberElectricityMeters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MemberElectricityMeterPlots_Plots_PlotId",
                        column: x => x.PlotId,
                        principalTable: "Plots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MemberElectricityReadings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MemberElectricityMeterId = table.Column<int>(type: "int", nullable: false),
                    ReadingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PreviousReading = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    CurrentReading = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Consumption = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    AppliedMemberRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    IsInitialReading = table.Column<bool>(type: "bit", nullable: false),
                    ChargeId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    SubmittedByMember = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberElectricityReadings", x => x.Id);
                    table.CheckConstraint("CK_MemberElectricityReadings_Amount_NonNegative", "[Amount] IS NULL OR [Amount] >= 0");
                    table.CheckConstraint("CK_MemberElectricityReadings_AppliedMemberRate_NonNegative", "[AppliedMemberRate] IS NULL OR [AppliedMemberRate] >= 0");
                    table.CheckConstraint("CK_MemberElectricityReadings_Consumption_NonNegative", "[Consumption] IS NULL OR [Consumption] >= 0");
                    table.CheckConstraint("CK_MemberElectricityReadings_CurrentReading_NonNegative", "[CurrentReading] >= 0");
                    table.CheckConstraint("CK_MemberElectricityReadings_PreviousReading_NonNegative", "[PreviousReading] IS NULL OR [PreviousReading] >= 0");
                    table.ForeignKey(
                        name: "FK_MemberElectricityReadings_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MemberElectricityReadings_Charges_ChargeId",
                        column: x => x.ChargeId,
                        principalTable: "Charges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MemberElectricityReadings_MemberElectricityMeters_MemberElectricityMeterId",
                        column: x => x.MemberElectricityMeterId,
                        principalTable: "MemberElectricityMeters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlotOwnerships_PlotId",
                table: "PlotOwnerships",
                column: "PlotId",
                unique: true,
                filter: "[ValidTo] IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Charges_Amount_Positive",
                table: "Charges",
                sql: "[Amount] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_AssociationElectricityReadings_CreatedByUserId",
                table: "AssociationElectricityReadings",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssociationElectricityReadings_IsInitialReading",
                table: "AssociationElectricityReadings",
                column: "IsInitialReading",
                unique: true,
                filter: "[IsInitialReading] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_AssociationElectricityReadings_ReadingDate",
                table: "AssociationElectricityReadings",
                column: "ReadingDate",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssociationElectricityTariffs_CreatedByUserId",
                table: "AssociationElectricityTariffs",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssociationElectricityTariffs_EffectiveFrom",
                table: "AssociationElectricityTariffs",
                column: "EffectiveFrom",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MemberElectricityMeterPlots_PlotId",
                table: "MemberElectricityMeterPlots",
                column: "PlotId");

            migrationBuilder.CreateIndex(
                name: "IX_MemberElectricityMeters_BillingPlotId",
                table: "MemberElectricityMeters",
                column: "BillingPlotId");

            migrationBuilder.CreateIndex(
                name: "IX_MemberElectricityMeters_CreatedByUserId",
                table: "MemberElectricityMeters",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MemberElectricityMeters_MemberId",
                table: "MemberElectricityMeters",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_MemberElectricityReadings_ChargeId",
                table: "MemberElectricityReadings",
                column: "ChargeId",
                unique: true,
                filter: "[ChargeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MemberElectricityReadings_CreatedByUserId",
                table: "MemberElectricityReadings",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MemberElectricityReadings_MemberElectricityMeterId_IsInitialReading",
                table: "MemberElectricityReadings",
                columns: new[] { "MemberElectricityMeterId", "IsInitialReading" },
                unique: true,
                filter: "[IsInitialReading] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_MemberElectricityReadings_MemberElectricityMeterId_ReadingDate",
                table: "MemberElectricityReadings",
                columns: new[] { "MemberElectricityMeterId", "ReadingDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MemberElectricityTariffs_CreatedByUserId",
                table: "MemberElectricityTariffs",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MemberElectricityTariffs_EffectiveFrom",
                table: "MemberElectricityTariffs",
                column: "EffectiveFrom",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssociationElectricityReadings");

            migrationBuilder.DropTable(
                name: "AssociationElectricityTariffs");

            migrationBuilder.DropTable(
                name: "MemberElectricityMeterPlots");

            migrationBuilder.DropTable(
                name: "MemberElectricityReadings");

            migrationBuilder.DropTable(
                name: "MemberElectricityTariffs");

            migrationBuilder.DropTable(
                name: "MemberElectricityMeters");

            migrationBuilder.DropIndex(
                name: "IX_PlotOwnerships_PlotId",
                table: "PlotOwnerships");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Charges_Amount_Positive",
                table: "Charges");

            migrationBuilder.CreateIndex(
                name: "IX_PlotOwnerships_PlotId_MemberId",
                table: "PlotOwnerships",
                columns: new[] { "PlotId", "MemberId" },
                unique: true,
                filter: "[ValidTo] IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Charges_Amount_Positive",
                table: "Charges",
                sql: "[Amount] > 0");
        }
    }
}
