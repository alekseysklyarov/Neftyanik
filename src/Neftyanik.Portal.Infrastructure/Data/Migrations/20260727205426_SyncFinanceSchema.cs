using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Neftyanik.Portal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SyncFinanceSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Charges_AspNetUsers_UserId",
                table: "Charges");

            migrationBuilder.DropForeignKey(
                name: "FK_Charges_ElectricityMeters_MeterId",
                table: "Charges");

            migrationBuilder.DropForeignKey(
                name: "FK_Charges_MeterReadings_SourceReadingId",
                table: "Charges");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_AspNetUsers_CancelledByUserId",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_AspNetUsers_UserId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_CancelledByUserId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_UserId_PaymentDate",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Charges_MeterId",
                table: "Charges");

            migrationBuilder.DropIndex(
                name: "IX_Charges_PlotId_PeriodYear_ChargeType",
                table: "Charges");

            migrationBuilder.DropIndex(
                name: "IX_Charges_SourceReadingId",
                table: "Charges");

            migrationBuilder.DropIndex(
                name: "IX_Charges_UserId_Status",
                table: "Charges");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "Comment",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "IsCancelled",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ChargeType",
                table: "Charges");

            migrationBuilder.DropColumn(
                name: "ChargedAt",
                table: "Charges");

            migrationBuilder.DropColumn(
                name: "MeterId",
                table: "Charges");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "Charges");

            migrationBuilder.DropColumn(
                name: "SourceReadingId",
                table: "Charges");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "Charges");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Charges");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "Charges",
                newName: "ChargeTypeId");

            migrationBuilder.AlterColumn<string>(
                name: "ReferenceNumber",
                table: "Payments",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CreatedByUserId",
                table: "Payments",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "Payments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "Payments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "Payments",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Payments",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlotId",
                table: "Payments",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "PeriodYear",
                table: "Charges",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Charges",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "Charges",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "Charges",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ChargeDate",
                table: "Charges",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "Charges",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateTable(
                name: "ChargeTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    DefaultAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChargeTypes", x => x.Id);
                    table.CheckConstraint("CK_ChargeTypes_DefaultAmount_Positive", "[DefaultAmount] IS NULL OR [DefaultAmount] > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_CancelledAtUtc",
                table: "Payments",
                column: "CancelledAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PaymentDate",
                table: "Payments",
                column: "PaymentDate");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PlotId",
                table: "Payments",
                column: "PlotId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ReferenceNumber",
                table: "Payments",
                column: "ReferenceNumber");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_Amount_Positive",
                table: "Payments",
                sql: "[Amount] > 0");

            migrationBuilder.CreateIndex(
                name: "IX_Charges_CancelledAtUtc",
                table: "Charges",
                column: "CancelledAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Charges_ChargeDate",
                table: "Charges",
                column: "ChargeDate");

            migrationBuilder.CreateIndex(
                name: "IX_Charges_ChargeTypeId",
                table: "Charges",
                column: "ChargeTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Charges_DueDate",
                table: "Charges",
                column: "DueDate");

            migrationBuilder.CreateIndex(
                name: "IX_Charges_PlotId",
                table: "Charges",
                column: "PlotId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Charges_Amount_Positive",
                table: "Charges",
                sql: "[Amount] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Charges_DueDate_NotEarlierThanChargeDate",
                table: "Charges",
                sql: "[DueDate] IS NULL OR [DueDate] >= [ChargeDate]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Charges_PeriodMonth_Range",
                table: "Charges",
                sql: "[PeriodMonth] IS NULL OR ([PeriodMonth] >= 1 AND [PeriodMonth] <= 12)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Charges_PeriodYear_Range",
                table: "Charges",
                sql: "[PeriodYear] IS NULL OR ([PeriodYear] >= 2000 AND [PeriodYear] <= 2100)");

            migrationBuilder.CreateIndex(
                name: "IX_ChargeTypes_Name",
                table: "ChargeTypes",
                column: "Name");

            migrationBuilder.AddForeignKey(
                name: "FK_Charges_ChargeTypes_ChargeTypeId",
                table: "Charges",
                column: "ChargeTypeId",
                principalTable: "ChargeTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Plots_PlotId",
                table: "Payments",
                column: "PlotId",
                principalTable: "Plots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Charges_ChargeTypes_ChargeTypeId",
                table: "Charges");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Plots_PlotId",
                table: "Payments");

            migrationBuilder.DropTable(
                name: "ChargeTypes");

            migrationBuilder.DropIndex(
                name: "IX_Payments_CancelledAtUtc",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_PaymentDate",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_PlotId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_ReferenceNumber",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_Amount_Positive",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Charges_CancelledAtUtc",
                table: "Charges");

            migrationBuilder.DropIndex(
                name: "IX_Charges_ChargeDate",
                table: "Charges");

            migrationBuilder.DropIndex(
                name: "IX_Charges_ChargeTypeId",
                table: "Charges");

            migrationBuilder.DropIndex(
                name: "IX_Charges_DueDate",
                table: "Charges");

            migrationBuilder.DropIndex(
                name: "IX_Charges_PlotId",
                table: "Charges");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Charges_Amount_Positive",
                table: "Charges");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Charges_DueDate_NotEarlierThanChargeDate",
                table: "Charges");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Charges_PeriodMonth_Range",
                table: "Charges");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Charges_PeriodYear_Range",
                table: "Charges");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PlotId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "Charges");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "Charges");

            migrationBuilder.DropColumn(
                name: "ChargeDate",
                table: "Charges");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "Charges");

            migrationBuilder.RenameColumn(
                name: "ChargeTypeId",
                table: "Charges",
                newName: "Status");

            migrationBuilder.AlterColumn<string>(
                name: "ReferenceNumber",
                table: "Payments",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(150)",
                oldMaxLength: 150,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CreatedByUserId",
                table: "Payments",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450,
                oldNullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                table: "Payments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledByUserId",
                table: "Payments",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Comment",
                table: "Payments",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Payments",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<bool>(
                name: "IsCancelled",
                table: "Payments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "Payments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "Payments",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<int>(
                name: "PeriodYear",
                table: "Charges",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Charges",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ChargeType",
                table: "Charges",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ChargedAt",
                table: "Charges",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<int>(
                name: "MeterId",
                table: "Charges",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Quantity",
                table: "Charges",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SourceReadingId",
                table: "Charges",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "Charges",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "Charges",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_CancelledByUserId",
                table: "Payments",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_UserId_PaymentDate",
                table: "Payments",
                columns: new[] { "UserId", "PaymentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Charges_MeterId",
                table: "Charges",
                column: "MeterId");

            migrationBuilder.CreateIndex(
                name: "IX_Charges_PlotId_PeriodYear_ChargeType",
                table: "Charges",
                columns: new[] { "PlotId", "PeriodYear", "ChargeType" });

            migrationBuilder.CreateIndex(
                name: "IX_Charges_SourceReadingId",
                table: "Charges",
                column: "SourceReadingId");

            migrationBuilder.CreateIndex(
                name: "IX_Charges_UserId_Status",
                table: "Charges",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_Charges_AspNetUsers_UserId",
                table: "Charges",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Charges_ElectricityMeters_MeterId",
                table: "Charges",
                column: "MeterId",
                principalTable: "ElectricityMeters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Charges_MeterReadings_SourceReadingId",
                table: "Charges",
                column: "SourceReadingId",
                principalTable: "MeterReadings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_AspNetUsers_CancelledByUserId",
                table: "Payments",
                column: "CancelledByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_AspNetUsers_UserId",
                table: "Payments",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
