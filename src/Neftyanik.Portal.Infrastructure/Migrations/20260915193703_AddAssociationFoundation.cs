using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Neftyanik.Portal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAssociationFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Associations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Associations", x => x.Id);
                });

            // The table is new, so this deterministic seed cannot collide with existing IDs.
            // Runtime compatibility resolves by slug, never by this numeric seed ID.
            migrationBuilder.InsertData(
                table: "Associations",
                columns: new[] { "Id", "CreatedAtUtc", "IsActive", "Name", "Slug" },
                values: new object[] { 1, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), true, "Нефтяник", "neftyanik" });

            migrationBuilder.DropForeignKey(
                name: "FK_Charges_ChargeTypes_ChargeTypeId",
                table: "Charges");

            migrationBuilder.DropForeignKey(
                name: "FK_Charges_Plots_PlotId",
                table: "Charges");

            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_AssociationElectricityReadings_AssociationElectricityReadingId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_ExpenseCategories_ExpenseCategoryId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_MemberElectricityMeters_Members_MemberId",
                table: "MemberElectricityMeters");

            migrationBuilder.DropForeignKey(
                name: "FK_MemberElectricityMeters_Plots_BillingPlotId",
                table: "MemberElectricityMeters");

            migrationBuilder.DropForeignKey(
                name: "FK_MemberElectricityReadings_Charges_ChargeId",
                table: "MemberElectricityReadings");

            migrationBuilder.DropForeignKey(
                name: "FK_MemberElectricityReadings_MemberElectricityMeters_MemberElectricityMeterId",
                table: "MemberElectricityReadings");

            migrationBuilder.DropForeignKey(
                name: "FK_PaymentAllocations_Charges_ChargeId",
                table: "PaymentAllocations");

            migrationBuilder.DropForeignKey(
                name: "FK_PaymentAllocations_Payments_PaymentId",
                table: "PaymentAllocations");

            migrationBuilder.DropForeignKey(
                name: "FK_PaymentNotifications_Members_MemberId",
                table: "PaymentNotifications");

            migrationBuilder.DropForeignKey(
                name: "FK_PaymentNotifications_Payments_PaymentId",
                table: "PaymentNotifications");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Members_MemberId",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Plots_PlotId",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_PlotOwnershipHistories_Plots_PlotId",
                table: "PlotOwnershipHistories");

            migrationBuilder.DropForeignKey(
                name: "FK_PlotOwnerships_Members_MemberId",
                table: "PlotOwnerships");

            migrationBuilder.DropForeignKey(
                name: "FK_PlotOwnerships_Plots_PlotId",
                table: "PlotOwnerships");

            migrationBuilder.DropForeignKey(
                name: "FK_Plots_MemberElectricityMeters_MemberElectricityMeterId",
                table: "Plots");

            migrationBuilder.DropIndex(
                name: "IX_SystemSettings_Key",
                table: "SystemSettings");

            migrationBuilder.DropIndex(
                name: "IX_Plots_CadastralNumber",
                table: "Plots");

            migrationBuilder.DropIndex(
                name: "IX_Plots_MemberElectricityMeterId",
                table: "Plots");

            migrationBuilder.DropIndex(
                name: "IX_Plots_Number",
                table: "Plots");

            migrationBuilder.DropIndex(
                name: "IX_PlotOwnerships_MemberId",
                table: "PlotOwnerships");

            migrationBuilder.DropIndex(
                name: "IX_PlotOwnerships_PlotId",
                table: "PlotOwnerships");

            migrationBuilder.DropIndex(
                name: "IX_PlotOwnershipHistories_PlotId",
                table: "PlotOwnershipHistories");

            migrationBuilder.DropIndex(
                name: "IX_Payments_CancelledAtUtc",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_MemberId",
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

            migrationBuilder.DropIndex(
                name: "IX_PaymentNotifications_CreatedAtUtc",
                table: "PaymentNotifications");

            migrationBuilder.DropIndex(
                name: "IX_PaymentNotifications_MemberId",
                table: "PaymentNotifications");

            migrationBuilder.DropIndex(
                name: "IX_PaymentNotifications_PaymentId",
                table: "PaymentNotifications");

            migrationBuilder.DropIndex(
                name: "IX_PaymentNotifications_Status",
                table: "PaymentNotifications");

            migrationBuilder.DropIndex(
                name: "IX_PaymentAllocations_ChargeId",
                table: "PaymentAllocations");

            migrationBuilder.DropIndex(
                name: "IX_PaymentAllocations_PaymentId",
                table: "PaymentAllocations");

            migrationBuilder.DropIndex(
                name: "IX_NewsArticles_IsPublished_PublishedAt",
                table: "NewsArticles");

            migrationBuilder.DropIndex(
                name: "IX_MembershipFeeRates_Year",
                table: "MembershipFeeRates");

            migrationBuilder.DropIndex(
                name: "IX_Members_Email",
                table: "Members");

            migrationBuilder.DropIndex(
                name: "IX_Members_FullName",
                table: "Members");

            migrationBuilder.DropIndex(
                name: "IX_MemberElectricityTariffs_EffectiveFrom",
                table: "MemberElectricityTariffs");

            migrationBuilder.DropIndex(
                name: "IX_MemberElectricityReadings_ChargeId",
                table: "MemberElectricityReadings");

            migrationBuilder.DropIndex(
                name: "IX_MemberElectricityReadings_MemberElectricityMeterId_IsInitialReading",
                table: "MemberElectricityReadings");

            migrationBuilder.DropIndex(
                name: "IX_MemberElectricityReadings_MemberElectricityMeterId_ReadingDate",
                table: "MemberElectricityReadings");

            migrationBuilder.DropIndex(
                name: "IX_MemberElectricityMeters_BillingPlotId",
                table: "MemberElectricityMeters");

            migrationBuilder.DropIndex(
                name: "IX_MemberElectricityMeters_MemberId",
                table: "MemberElectricityMeters");

            migrationBuilder.DropIndex(
                name: "IX_FinancialAuditLogs_CreatedAtUtc",
                table: "FinancialAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_FinancialAuditLogs_EntityType_EntityId",
                table: "FinancialAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_AssociationElectricityReadingId",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_ExpenseCategoryId",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_ExpenseDate",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_ChargeTypes_Code",
                table: "ChargeTypes");

            migrationBuilder.DropIndex(
                name: "IX_ChargeTypes_IsDefault",
                table: "ChargeTypes");

            migrationBuilder.DropIndex(
                name: "IX_ChargeTypes_Name",
                table: "ChargeTypes");

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

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_EntityType_EntityId",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AssociationElectricityTariffs_EffectiveFrom",
                table: "AssociationElectricityTariffs");

            migrationBuilder.DropIndex(
                name: "IX_AssociationElectricityReadings_IsInitialReading",
                table: "AssociationElectricityReadings");

            migrationBuilder.DropIndex(
                name: "IX_AssociationElectricityReadings_ReadingDate",
                table: "AssociationElectricityReadings");

            // Keep this historical table list self-contained; do not derive it from the runtime model.
            string[] tenantTables =
            [
                "AssociationDocuments", "AssociationElectricityReadings", "AssociationElectricityTariffs",
                "AuditLogs", "Charges", "ChargeTypes", "ExpenseCategories", "Expenses", "FinancialAuditLogs",
                "MemberElectricityMeters", "MemberElectricityReadings", "MemberElectricityTariffs", "Members",
                "MembershipFeeRates", "NewsArticles", "PaymentAllocations", "PaymentNotifications", "Payments",
                "PlotOwnershipHistories", "PlotOwnerships", "Plots", "SystemSettings"
            ];

            foreach (var table in tenantTables)
            {
                migrationBuilder.AddColumn<int>(
                    name: "AssociationId", table: table, type: "int", nullable: true);
            }

            foreach (var table in tenantTables)
            {
                // Dynamic SQL also avoids SQL Server binding the new column before ALTER TABLE
                // when this migration is emitted as an idempotent script.
                migrationBuilder.Sql($"""
                    EXEC(N'UPDATE [{table}] SET [AssociationId] =
                        (SELECT [Id] FROM [Associations] WHERE [Slug] = ''neftyanik'')
                        WHERE [AssociationId] IS NULL');
                    """);
            }

            foreach (var table in tenantTables)
            {
                migrationBuilder.AlterColumn<int>(
                    name: "AssociationId", table: table, type: "int", nullable: false,
                    oldClrType: typeof(int), oldType: "int", oldNullable: true);
            }

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Plots_AssociationId_Id",
                table: "Plots",
                columns: new[] { "AssociationId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Payments_AssociationId_Id",
                table: "Payments",
                columns: new[] { "AssociationId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Members_AssociationId_Id",
                table: "Members",
                columns: new[] { "AssociationId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_MemberElectricityMeters_AssociationId_Id",
                table: "MemberElectricityMeters",
                columns: new[] { "AssociationId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ExpenseCategories_AssociationId_Id",
                table: "ExpenseCategories",
                columns: new[] { "AssociationId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ChargeTypes_AssociationId_Id",
                table: "ChargeTypes",
                columns: new[] { "AssociationId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Charges_AssociationId_Id",
                table: "Charges",
                columns: new[] { "AssociationId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_AssociationElectricityReadings_AssociationId_Id",
                table: "AssociationElectricityReadings",
                columns: new[] { "AssociationId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SystemSettings_AssociationId_Key",
                table: "SystemSettings",
                columns: new[] { "AssociationId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Plots_AssociationId_CadastralNumber",
                table: "Plots",
                columns: new[] { "AssociationId", "CadastralNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_Plots_AssociationId_MemberElectricityMeterId",
                table: "Plots",
                columns: new[] { "AssociationId", "MemberElectricityMeterId" });

            migrationBuilder.CreateIndex(
                name: "IX_Plots_AssociationId_Number",
                table: "Plots",
                columns: new[] { "AssociationId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlotOwnerships_AssociationId_MemberId",
                table: "PlotOwnerships",
                columns: new[] { "AssociationId", "MemberId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlotOwnerships_AssociationId_PlotId",
                table: "PlotOwnerships",
                columns: new[] { "AssociationId", "PlotId" },
                unique: true,
                filter: "[ValidTo] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PlotOwnershipHistories_AssociationId_PlotId",
                table: "PlotOwnershipHistories",
                columns: new[] { "AssociationId", "PlotId" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_AssociationId_CancelledAtUtc",
                table: "Payments",
                columns: new[] { "AssociationId", "CancelledAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_AssociationId_MemberId",
                table: "Payments",
                columns: new[] { "AssociationId", "MemberId" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_AssociationId_PaymentDate",
                table: "Payments",
                columns: new[] { "AssociationId", "PaymentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_AssociationId_PlotId",
                table: "Payments",
                columns: new[] { "AssociationId", "PlotId" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_AssociationId_ReferenceNumber",
                table: "Payments",
                columns: new[] { "AssociationId", "ReferenceNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentNotifications_AssociationId_CreatedAtUtc",
                table: "PaymentNotifications",
                columns: new[] { "AssociationId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentNotifications_AssociationId_MemberId",
                table: "PaymentNotifications",
                columns: new[] { "AssociationId", "MemberId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentNotifications_AssociationId_PaymentId",
                table: "PaymentNotifications",
                columns: new[] { "AssociationId", "PaymentId" },
                unique: true,
                filter: "[PaymentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentNotifications_AssociationId_Status",
                table: "PaymentNotifications",
                columns: new[] { "AssociationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_AssociationId_ChargeId",
                table: "PaymentAllocations",
                columns: new[] { "AssociationId", "ChargeId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_AssociationId_PaymentId",
                table: "PaymentAllocations",
                columns: new[] { "AssociationId", "PaymentId" });

            migrationBuilder.CreateIndex(
                name: "IX_NewsArticles_AssociationId_IsPublished_PublishedAt",
                table: "NewsArticles",
                columns: new[] { "AssociationId", "IsPublished", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MembershipFeeRates_AssociationId_Year",
                table: "MembershipFeeRates",
                columns: new[] { "AssociationId", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Members_AssociationId_Email",
                table: "Members",
                columns: new[] { "AssociationId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_Members_AssociationId_FullName",
                table: "Members",
                columns: new[] { "AssociationId", "FullName" });

            migrationBuilder.CreateIndex(
                name: "IX_MemberElectricityTariffs_AssociationId_EffectiveFrom",
                table: "MemberElectricityTariffs",
                columns: new[] { "AssociationId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MemberElectricityReadings_AssociationId_ChargeId",
                table: "MemberElectricityReadings",
                columns: new[] { "AssociationId", "ChargeId" },
                unique: true,
                filter: "[ChargeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MemberElectricityReadings_AssociationId_MemberElectricityMeterId_IsInitialReading",
                table: "MemberElectricityReadings",
                columns: new[] { "AssociationId", "MemberElectricityMeterId", "IsInitialReading" },
                unique: true,
                filter: "[IsInitialReading] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_MemberElectricityReadings_AssociationId_MemberElectricityMeterId_ReadingDate",
                table: "MemberElectricityReadings",
                columns: new[] { "AssociationId", "MemberElectricityMeterId", "ReadingDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MemberElectricityMeters_AssociationId_BillingPlotId",
                table: "MemberElectricityMeters",
                columns: new[] { "AssociationId", "BillingPlotId" });

            migrationBuilder.CreateIndex(
                name: "IX_MemberElectricityMeters_AssociationId_MemberId",
                table: "MemberElectricityMeters",
                columns: new[] { "AssociationId", "MemberId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialAuditLogs_AssociationId_CreatedAtUtc",
                table: "FinancialAuditLogs",
                columns: new[] { "AssociationId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialAuditLogs_AssociationId_EntityType_EntityId",
                table: "FinancialAuditLogs",
                columns: new[] { "AssociationId", "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_AssociationId_AssociationElectricityReadingId",
                table: "Expenses",
                columns: new[] { "AssociationId", "AssociationElectricityReadingId" },
                unique: true,
                filter: "[AssociationElectricityReadingId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_AssociationId_ExpenseCategoryId",
                table: "Expenses",
                columns: new[] { "AssociationId", "ExpenseCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_AssociationId_ExpenseDate",
                table: "Expenses",
                columns: new[] { "AssociationId", "ExpenseDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ChargeTypes_AssociationId_Code",
                table: "ChargeTypes",
                columns: new[] { "AssociationId", "Code" },
                unique: true,
                filter: "[Code] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ChargeTypes_AssociationId_IsDefault",
                table: "ChargeTypes",
                columns: new[] { "AssociationId", "IsDefault" },
                unique: true,
                filter: "[IsDefault] = 1 AND [IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ChargeTypes_AssociationId_Name",
                table: "ChargeTypes",
                columns: new[] { "AssociationId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Charges_AssociationId_CancelledAtUtc",
                table: "Charges",
                columns: new[] { "AssociationId", "CancelledAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Charges_AssociationId_ChargeDate",
                table: "Charges",
                columns: new[] { "AssociationId", "ChargeDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Charges_AssociationId_ChargeTypeId",
                table: "Charges",
                columns: new[] { "AssociationId", "ChargeTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Charges_AssociationId_DueDate",
                table: "Charges",
                columns: new[] { "AssociationId", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Charges_AssociationId_PlotId",
                table: "Charges",
                columns: new[] { "AssociationId", "PlotId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_AssociationId_EntityType_EntityId",
                table: "AuditLogs",
                columns: new[] { "AssociationId", "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssociationElectricityTariffs_AssociationId_EffectiveFrom",
                table: "AssociationElectricityTariffs",
                columns: new[] { "AssociationId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssociationElectricityReadings_AssociationId_IsInitialReading",
                table: "AssociationElectricityReadings",
                columns: new[] { "AssociationId", "IsInitialReading" },
                unique: true,
                filter: "[IsInitialReading] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_AssociationElectricityReadings_AssociationId_ReadingDate",
                table: "AssociationElectricityReadings",
                columns: new[] { "AssociationId", "ReadingDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssociationDocuments_AssociationId",
                table: "AssociationDocuments",
                column: "AssociationId");

            migrationBuilder.CreateIndex(
                name: "IX_Associations_Slug",
                table: "Associations",
                column: "Slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AssociationDocuments_Associations_AssociationId",
                table: "AssociationDocuments",
                column: "AssociationId",
                principalTable: "Associations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AssociationElectricityReadings_Associations_AssociationId",
                table: "AssociationElectricityReadings",
                column: "AssociationId",
                principalTable: "Associations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AssociationElectricityTariffs_Associations_AssociationId",
                table: "AssociationElectricityTariffs",
                column: "AssociationId",
                principalTable: "Associations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_Associations_AssociationId",
                table: "AuditLogs",
                column: "AssociationId",
                principalTable: "Associations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Charges_Associations_AssociationId",
                table: "Charges",
                column: "AssociationId",
                principalTable: "Associations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Charges_ChargeTypes_AssociationId_ChargeTypeId",
                table: "Charges",
                columns: new[] { "AssociationId", "ChargeTypeId" },
                principalTable: "ChargeTypes",
                principalColumns: new[] { "AssociationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Charges_Plots_AssociationId_PlotId",
                table: "Charges",
                columns: new[] { "AssociationId", "PlotId" },
                principalTable: "Plots",
                principalColumns: new[] { "AssociationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ChargeTypes_Associations_AssociationId",
                table: "ChargeTypes",
                column: "AssociationId",
                principalTable: "Associations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ExpenseCategories_Associations_AssociationId",
                table: "ExpenseCategories",
                column: "AssociationId",
                principalTable: "Associations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_AssociationElectricityReadings_AssociationId_AssociationElectricityReadingId",
                table: "Expenses",
                columns: new[] { "AssociationId", "AssociationElectricityReadingId" },
                principalTable: "AssociationElectricityReadings",
                principalColumns: new[] { "AssociationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_Associations_AssociationId",
                table: "Expenses",
                column: "AssociationId",
                principalTable: "Associations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_ExpenseCategories_AssociationId_ExpenseCategoryId",
                table: "Expenses",
                columns: new[] { "AssociationId", "ExpenseCategoryId" },
                principalTable: "ExpenseCategories",
                principalColumns: new[] { "AssociationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FinancialAuditLogs_Associations_AssociationId",
                table: "FinancialAuditLogs",
                column: "AssociationId",
                principalTable: "Associations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MemberElectricityMeters_Associations_AssociationId",
                table: "MemberElectricityMeters",
                column: "AssociationId",
                principalTable: "Associations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MemberElectricityMeters_Members_AssociationId_MemberId",
                table: "MemberElectricityMeters",
                columns: new[] { "AssociationId", "MemberId" },
                principalTable: "Members",
                principalColumns: new[] { "AssociationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MemberElectricityMeters_Plots_AssociationId_BillingPlotId",
                table: "MemberElectricityMeters",
                columns: new[] { "AssociationId", "BillingPlotId" },
                principalTable: "Plots",
                principalColumns: new[] { "AssociationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MemberElectricityReadings_Associations_AssociationId",
                table: "MemberElectricityReadings",
                column: "AssociationId",
                principalTable: "Associations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MemberElectricityReadings_Charges_AssociationId_ChargeId",
                table: "MemberElectricityReadings",
                columns: new[] { "AssociationId", "ChargeId" },
                principalTable: "Charges",
                principalColumns: new[] { "AssociationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MemberElectricityReadings_MemberElectricityMeters_AssociationId_MemberElectricityMeterId",
                table: "MemberElectricityReadings",
                columns: new[] { "AssociationId", "MemberElectricityMeterId" },
                principalTable: "MemberElectricityMeters",
                principalColumns: new[] { "AssociationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MemberElectricityTariffs_Associations_AssociationId",
                table: "MemberElectricityTariffs",
                column: "AssociationId",
                principalTable: "Associations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Members_Associations_AssociationId",
                table: "Members",
                column: "AssociationId",
                principalTable: "Associations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MembershipFeeRates_Associations_AssociationId",
                table: "MembershipFeeRates",
                column: "AssociationId",
                principalTable: "Associations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_NewsArticles_Associations_AssociationId",
                table: "NewsArticles",
                column: "AssociationId",
                principalTable: "Associations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentAllocations_Associations_AssociationId",
                table: "PaymentAllocations",
                column: "AssociationId",
                principalTable: "Associations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentAllocations_Charges_AssociationId_ChargeId",
                table: "PaymentAllocations",
                columns: new[] { "AssociationId", "ChargeId" },
                principalTable: "Charges",
                principalColumns: new[] { "AssociationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentAllocations_Payments_AssociationId_PaymentId",
                table: "PaymentAllocations",
                columns: new[] { "AssociationId", "PaymentId" },
                principalTable: "Payments",
                principalColumns: new[] { "AssociationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentNotifications_Associations_AssociationId",
                table: "PaymentNotifications",
                column: "AssociationId",
                principalTable: "Associations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentNotifications_Members_AssociationId_MemberId",
                table: "PaymentNotifications",
                columns: new[] { "AssociationId", "MemberId" },
                principalTable: "Members",
                principalColumns: new[] { "AssociationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentNotifications_Payments_AssociationId_PaymentId",
                table: "PaymentNotifications",
                columns: new[] { "AssociationId", "PaymentId" },
                principalTable: "Payments",
                principalColumns: new[] { "AssociationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Associations_AssociationId",
                table: "Payments",
                column: "AssociationId",
                principalTable: "Associations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Members_AssociationId_MemberId",
                table: "Payments",
                columns: new[] { "AssociationId", "MemberId" },
                principalTable: "Members",
                principalColumns: new[] { "AssociationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Plots_AssociationId_PlotId",
                table: "Payments",
                columns: new[] { "AssociationId", "PlotId" },
                principalTable: "Plots",
                principalColumns: new[] { "AssociationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PlotOwnershipHistories_Associations_AssociationId",
                table: "PlotOwnershipHistories",
                column: "AssociationId",
                principalTable: "Associations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PlotOwnershipHistories_Plots_AssociationId_PlotId",
                table: "PlotOwnershipHistories",
                columns: new[] { "AssociationId", "PlotId" },
                principalTable: "Plots",
                principalColumns: new[] { "AssociationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PlotOwnerships_Associations_AssociationId",
                table: "PlotOwnerships",
                column: "AssociationId",
                principalTable: "Associations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PlotOwnerships_Members_AssociationId_MemberId",
                table: "PlotOwnerships",
                columns: new[] { "AssociationId", "MemberId" },
                principalTable: "Members",
                principalColumns: new[] { "AssociationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PlotOwnerships_Plots_AssociationId_PlotId",
                table: "PlotOwnerships",
                columns: new[] { "AssociationId", "PlotId" },
                principalTable: "Plots",
                principalColumns: new[] { "AssociationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Plots_Associations_AssociationId",
                table: "Plots",
                column: "AssociationId",
                principalTable: "Associations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Plots_MemberElectricityMeters_AssociationId_MemberElectricityMeterId",
                table: "Plots",
                columns: new[] { "AssociationId", "MemberElectricityMeterId" },
                principalTable: "MemberElectricityMeters",
                principalColumns: new[] { "AssociationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SystemSettings_Associations_AssociationId",
                table: "SystemSettings",
                column: "AssociationId",
                principalTable: "Associations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [Associations] WHERE [Slug] <> 'neftyanik')
                    THROW 51000, 'Cannot remove association ownership after another association has been created.', 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_AssociationDocuments_Associations_AssociationId",
                table: "AssociationDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_AssociationElectricityReadings_Associations_AssociationId",
                table: "AssociationElectricityReadings");

            migrationBuilder.DropForeignKey(
                name: "FK_AssociationElectricityTariffs_Associations_AssociationId",
                table: "AssociationElectricityTariffs");

            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_Associations_AssociationId",
                table: "AuditLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_Charges_Associations_AssociationId",
                table: "Charges");

            migrationBuilder.DropForeignKey(
                name: "FK_Charges_ChargeTypes_AssociationId_ChargeTypeId",
                table: "Charges");

            migrationBuilder.DropForeignKey(
                name: "FK_Charges_Plots_AssociationId_PlotId",
                table: "Charges");

            migrationBuilder.DropForeignKey(
                name: "FK_ChargeTypes_Associations_AssociationId",
                table: "ChargeTypes");

            migrationBuilder.DropForeignKey(
                name: "FK_ExpenseCategories_Associations_AssociationId",
                table: "ExpenseCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_AssociationElectricityReadings_AssociationId_AssociationElectricityReadingId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_Associations_AssociationId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_ExpenseCategories_AssociationId_ExpenseCategoryId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_FinancialAuditLogs_Associations_AssociationId",
                table: "FinancialAuditLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_MemberElectricityMeters_Associations_AssociationId",
                table: "MemberElectricityMeters");

            migrationBuilder.DropForeignKey(
                name: "FK_MemberElectricityMeters_Members_AssociationId_MemberId",
                table: "MemberElectricityMeters");

            migrationBuilder.DropForeignKey(
                name: "FK_MemberElectricityMeters_Plots_AssociationId_BillingPlotId",
                table: "MemberElectricityMeters");

            migrationBuilder.DropForeignKey(
                name: "FK_MemberElectricityReadings_Associations_AssociationId",
                table: "MemberElectricityReadings");

            migrationBuilder.DropForeignKey(
                name: "FK_MemberElectricityReadings_Charges_AssociationId_ChargeId",
                table: "MemberElectricityReadings");

            migrationBuilder.DropForeignKey(
                name: "FK_MemberElectricityReadings_MemberElectricityMeters_AssociationId_MemberElectricityMeterId",
                table: "MemberElectricityReadings");

            migrationBuilder.DropForeignKey(
                name: "FK_MemberElectricityTariffs_Associations_AssociationId",
                table: "MemberElectricityTariffs");

            migrationBuilder.DropForeignKey(
                name: "FK_Members_Associations_AssociationId",
                table: "Members");

            migrationBuilder.DropForeignKey(
                name: "FK_MembershipFeeRates_Associations_AssociationId",
                table: "MembershipFeeRates");

            migrationBuilder.DropForeignKey(
                name: "FK_NewsArticles_Associations_AssociationId",
                table: "NewsArticles");

            migrationBuilder.DropForeignKey(
                name: "FK_PaymentAllocations_Associations_AssociationId",
                table: "PaymentAllocations");

            migrationBuilder.DropForeignKey(
                name: "FK_PaymentAllocations_Charges_AssociationId_ChargeId",
                table: "PaymentAllocations");

            migrationBuilder.DropForeignKey(
                name: "FK_PaymentAllocations_Payments_AssociationId_PaymentId",
                table: "PaymentAllocations");

            migrationBuilder.DropForeignKey(
                name: "FK_PaymentNotifications_Associations_AssociationId",
                table: "PaymentNotifications");

            migrationBuilder.DropForeignKey(
                name: "FK_PaymentNotifications_Members_AssociationId_MemberId",
                table: "PaymentNotifications");

            migrationBuilder.DropForeignKey(
                name: "FK_PaymentNotifications_Payments_AssociationId_PaymentId",
                table: "PaymentNotifications");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Associations_AssociationId",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Members_AssociationId_MemberId",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Plots_AssociationId_PlotId",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_PlotOwnershipHistories_Associations_AssociationId",
                table: "PlotOwnershipHistories");

            migrationBuilder.DropForeignKey(
                name: "FK_PlotOwnershipHistories_Plots_AssociationId_PlotId",
                table: "PlotOwnershipHistories");

            migrationBuilder.DropForeignKey(
                name: "FK_PlotOwnerships_Associations_AssociationId",
                table: "PlotOwnerships");

            migrationBuilder.DropForeignKey(
                name: "FK_PlotOwnerships_Members_AssociationId_MemberId",
                table: "PlotOwnerships");

            migrationBuilder.DropForeignKey(
                name: "FK_PlotOwnerships_Plots_AssociationId_PlotId",
                table: "PlotOwnerships");

            migrationBuilder.DropForeignKey(
                name: "FK_Plots_Associations_AssociationId",
                table: "Plots");

            migrationBuilder.DropForeignKey(
                name: "FK_Plots_MemberElectricityMeters_AssociationId_MemberElectricityMeterId",
                table: "Plots");

            migrationBuilder.DropForeignKey(
                name: "FK_SystemSettings_Associations_AssociationId",
                table: "SystemSettings");

            migrationBuilder.DropTable(
                name: "Associations");

            migrationBuilder.DropIndex(
                name: "IX_SystemSettings_AssociationId_Key",
                table: "SystemSettings");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Plots_AssociationId_Id",
                table: "Plots");

            migrationBuilder.DropIndex(
                name: "IX_Plots_AssociationId_CadastralNumber",
                table: "Plots");

            migrationBuilder.DropIndex(
                name: "IX_Plots_AssociationId_MemberElectricityMeterId",
                table: "Plots");

            migrationBuilder.DropIndex(
                name: "IX_Plots_AssociationId_Number",
                table: "Plots");

            migrationBuilder.DropIndex(
                name: "IX_PlotOwnerships_AssociationId_MemberId",
                table: "PlotOwnerships");

            migrationBuilder.DropIndex(
                name: "IX_PlotOwnerships_AssociationId_PlotId",
                table: "PlotOwnerships");

            migrationBuilder.DropIndex(
                name: "IX_PlotOwnershipHistories_AssociationId_PlotId",
                table: "PlotOwnershipHistories");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Payments_AssociationId_Id",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_AssociationId_CancelledAtUtc",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_AssociationId_MemberId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_AssociationId_PaymentDate",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_AssociationId_PlotId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_AssociationId_ReferenceNumber",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_PaymentNotifications_AssociationId_CreatedAtUtc",
                table: "PaymentNotifications");

            migrationBuilder.DropIndex(
                name: "IX_PaymentNotifications_AssociationId_MemberId",
                table: "PaymentNotifications");

            migrationBuilder.DropIndex(
                name: "IX_PaymentNotifications_AssociationId_PaymentId",
                table: "PaymentNotifications");

            migrationBuilder.DropIndex(
                name: "IX_PaymentNotifications_AssociationId_Status",
                table: "PaymentNotifications");

            migrationBuilder.DropIndex(
                name: "IX_PaymentAllocations_AssociationId_ChargeId",
                table: "PaymentAllocations");

            migrationBuilder.DropIndex(
                name: "IX_PaymentAllocations_AssociationId_PaymentId",
                table: "PaymentAllocations");

            migrationBuilder.DropIndex(
                name: "IX_NewsArticles_AssociationId_IsPublished_PublishedAt",
                table: "NewsArticles");

            migrationBuilder.DropIndex(
                name: "IX_MembershipFeeRates_AssociationId_Year",
                table: "MembershipFeeRates");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Members_AssociationId_Id",
                table: "Members");

            migrationBuilder.DropIndex(
                name: "IX_Members_AssociationId_Email",
                table: "Members");

            migrationBuilder.DropIndex(
                name: "IX_Members_AssociationId_FullName",
                table: "Members");

            migrationBuilder.DropIndex(
                name: "IX_MemberElectricityTariffs_AssociationId_EffectiveFrom",
                table: "MemberElectricityTariffs");

            migrationBuilder.DropIndex(
                name: "IX_MemberElectricityReadings_AssociationId_ChargeId",
                table: "MemberElectricityReadings");

            migrationBuilder.DropIndex(
                name: "IX_MemberElectricityReadings_AssociationId_MemberElectricityMeterId_IsInitialReading",
                table: "MemberElectricityReadings");

            migrationBuilder.DropIndex(
                name: "IX_MemberElectricityReadings_AssociationId_MemberElectricityMeterId_ReadingDate",
                table: "MemberElectricityReadings");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_MemberElectricityMeters_AssociationId_Id",
                table: "MemberElectricityMeters");

            migrationBuilder.DropIndex(
                name: "IX_MemberElectricityMeters_AssociationId_BillingPlotId",
                table: "MemberElectricityMeters");

            migrationBuilder.DropIndex(
                name: "IX_MemberElectricityMeters_AssociationId_MemberId",
                table: "MemberElectricityMeters");

            migrationBuilder.DropIndex(
                name: "IX_FinancialAuditLogs_AssociationId_CreatedAtUtc",
                table: "FinancialAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_FinancialAuditLogs_AssociationId_EntityType_EntityId",
                table: "FinancialAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_AssociationId_AssociationElectricityReadingId",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_AssociationId_ExpenseCategoryId",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_AssociationId_ExpenseDate",
                table: "Expenses");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ExpenseCategories_AssociationId_Id",
                table: "ExpenseCategories");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ChargeTypes_AssociationId_Id",
                table: "ChargeTypes");

            migrationBuilder.DropIndex(
                name: "IX_ChargeTypes_AssociationId_Code",
                table: "ChargeTypes");

            migrationBuilder.DropIndex(
                name: "IX_ChargeTypes_AssociationId_IsDefault",
                table: "ChargeTypes");

            migrationBuilder.DropIndex(
                name: "IX_ChargeTypes_AssociationId_Name",
                table: "ChargeTypes");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Charges_AssociationId_Id",
                table: "Charges");

            migrationBuilder.DropIndex(
                name: "IX_Charges_AssociationId_CancelledAtUtc",
                table: "Charges");

            migrationBuilder.DropIndex(
                name: "IX_Charges_AssociationId_ChargeDate",
                table: "Charges");

            migrationBuilder.DropIndex(
                name: "IX_Charges_AssociationId_ChargeTypeId",
                table: "Charges");

            migrationBuilder.DropIndex(
                name: "IX_Charges_AssociationId_DueDate",
                table: "Charges");

            migrationBuilder.DropIndex(
                name: "IX_Charges_AssociationId_PlotId",
                table: "Charges");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_AssociationId_EntityType_EntityId",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AssociationElectricityTariffs_AssociationId_EffectiveFrom",
                table: "AssociationElectricityTariffs");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AssociationElectricityReadings_AssociationId_Id",
                table: "AssociationElectricityReadings");

            migrationBuilder.DropIndex(
                name: "IX_AssociationElectricityReadings_AssociationId_IsInitialReading",
                table: "AssociationElectricityReadings");

            migrationBuilder.DropIndex(
                name: "IX_AssociationElectricityReadings_AssociationId_ReadingDate",
                table: "AssociationElectricityReadings");

            migrationBuilder.DropIndex(
                name: "IX_AssociationDocuments_AssociationId",
                table: "AssociationDocuments");

            migrationBuilder.DropColumn(
                name: "AssociationId",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "AssociationId",
                table: "Plots");

            migrationBuilder.DropColumn(
                name: "AssociationId",
                table: "PlotOwnerships");

            migrationBuilder.DropColumn(
                name: "AssociationId",
                table: "PlotOwnershipHistories");

            migrationBuilder.DropColumn(
                name: "AssociationId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "AssociationId",
                table: "PaymentNotifications");

            migrationBuilder.DropColumn(
                name: "AssociationId",
                table: "PaymentAllocations");

            migrationBuilder.DropColumn(
                name: "AssociationId",
                table: "NewsArticles");

            migrationBuilder.DropColumn(
                name: "AssociationId",
                table: "MembershipFeeRates");

            migrationBuilder.DropColumn(
                name: "AssociationId",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "AssociationId",
                table: "MemberElectricityTariffs");

            migrationBuilder.DropColumn(
                name: "AssociationId",
                table: "MemberElectricityReadings");

            migrationBuilder.DropColumn(
                name: "AssociationId",
                table: "MemberElectricityMeters");

            migrationBuilder.DropColumn(
                name: "AssociationId",
                table: "FinancialAuditLogs");

            migrationBuilder.DropColumn(
                name: "AssociationId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "AssociationId",
                table: "ExpenseCategories");

            migrationBuilder.DropColumn(
                name: "AssociationId",
                table: "ChargeTypes");

            migrationBuilder.DropColumn(
                name: "AssociationId",
                table: "Charges");

            migrationBuilder.DropColumn(
                name: "AssociationId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "AssociationId",
                table: "AssociationElectricityTariffs");

            migrationBuilder.DropColumn(
                name: "AssociationId",
                table: "AssociationElectricityReadings");

            migrationBuilder.DropColumn(
                name: "AssociationId",
                table: "AssociationDocuments");

            migrationBuilder.CreateIndex(
                name: "IX_SystemSettings_Key",
                table: "SystemSettings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Plots_CadastralNumber",
                table: "Plots",
                column: "CadastralNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Plots_MemberElectricityMeterId",
                table: "Plots",
                column: "MemberElectricityMeterId");

            migrationBuilder.CreateIndex(
                name: "IX_Plots_Number",
                table: "Plots",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlotOwnerships_MemberId",
                table: "PlotOwnerships",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_PlotOwnerships_PlotId",
                table: "PlotOwnerships",
                column: "PlotId",
                unique: true,
                filter: "[ValidTo] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PlotOwnershipHistories_PlotId",
                table: "PlotOwnershipHistories",
                column: "PlotId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_CancelledAtUtc",
                table: "Payments",
                column: "CancelledAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_MemberId",
                table: "Payments",
                column: "MemberId");

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

            migrationBuilder.CreateIndex(
                name: "IX_PaymentNotifications_CreatedAtUtc",
                table: "PaymentNotifications",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentNotifications_MemberId",
                table: "PaymentNotifications",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentNotifications_PaymentId",
                table: "PaymentNotifications",
                column: "PaymentId",
                unique: true,
                filter: "[PaymentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentNotifications_Status",
                table: "PaymentNotifications",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_ChargeId",
                table: "PaymentAllocations",
                column: "ChargeId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_PaymentId",
                table: "PaymentAllocations",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_NewsArticles_IsPublished_PublishedAt",
                table: "NewsArticles",
                columns: new[] { "IsPublished", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MembershipFeeRates_Year",
                table: "MembershipFeeRates",
                column: "Year",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Members_Email",
                table: "Members",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Members_FullName",
                table: "Members",
                column: "FullName");

            migrationBuilder.CreateIndex(
                name: "IX_MemberElectricityTariffs_EffectiveFrom",
                table: "MemberElectricityTariffs",
                column: "EffectiveFrom",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MemberElectricityReadings_ChargeId",
                table: "MemberElectricityReadings",
                column: "ChargeId",
                unique: true,
                filter: "[ChargeId] IS NOT NULL");

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
                name: "IX_MemberElectricityMeters_BillingPlotId",
                table: "MemberElectricityMeters",
                column: "BillingPlotId");

            migrationBuilder.CreateIndex(
                name: "IX_MemberElectricityMeters_MemberId",
                table: "MemberElectricityMeters",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialAuditLogs_CreatedAtUtc",
                table: "FinancialAuditLogs",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialAuditLogs_EntityType_EntityId",
                table: "FinancialAuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_AssociationElectricityReadingId",
                table: "Expenses",
                column: "AssociationElectricityReadingId",
                unique: true,
                filter: "[AssociationElectricityReadingId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ExpenseCategoryId",
                table: "Expenses",
                column: "ExpenseCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ExpenseDate",
                table: "Expenses",
                column: "ExpenseDate");

            migrationBuilder.CreateIndex(
                name: "IX_ChargeTypes_Code",
                table: "ChargeTypes",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ChargeTypes_IsDefault",
                table: "ChargeTypes",
                column: "IsDefault",
                unique: true,
                filter: "[IsDefault] = 1 AND [IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ChargeTypes_Name",
                table: "ChargeTypes",
                column: "Name");

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

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_EntityId",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssociationElectricityTariffs_EffectiveFrom",
                table: "AssociationElectricityTariffs",
                column: "EffectiveFrom",
                unique: true);

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

            migrationBuilder.AddForeignKey(
                name: "FK_Charges_ChargeTypes_ChargeTypeId",
                table: "Charges",
                column: "ChargeTypeId",
                principalTable: "ChargeTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Charges_Plots_PlotId",
                table: "Charges",
                column: "PlotId",
                principalTable: "Plots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_AssociationElectricityReadings_AssociationElectricityReadingId",
                table: "Expenses",
                column: "AssociationElectricityReadingId",
                principalTable: "AssociationElectricityReadings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_ExpenseCategories_ExpenseCategoryId",
                table: "Expenses",
                column: "ExpenseCategoryId",
                principalTable: "ExpenseCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MemberElectricityMeters_Members_MemberId",
                table: "MemberElectricityMeters",
                column: "MemberId",
                principalTable: "Members",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MemberElectricityMeters_Plots_BillingPlotId",
                table: "MemberElectricityMeters",
                column: "BillingPlotId",
                principalTable: "Plots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MemberElectricityReadings_Charges_ChargeId",
                table: "MemberElectricityReadings",
                column: "ChargeId",
                principalTable: "Charges",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MemberElectricityReadings_MemberElectricityMeters_MemberElectricityMeterId",
                table: "MemberElectricityReadings",
                column: "MemberElectricityMeterId",
                principalTable: "MemberElectricityMeters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentAllocations_Charges_ChargeId",
                table: "PaymentAllocations",
                column: "ChargeId",
                principalTable: "Charges",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentAllocations_Payments_PaymentId",
                table: "PaymentAllocations",
                column: "PaymentId",
                principalTable: "Payments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentNotifications_Members_MemberId",
                table: "PaymentNotifications",
                column: "MemberId",
                principalTable: "Members",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentNotifications_Payments_PaymentId",
                table: "PaymentNotifications",
                column: "PaymentId",
                principalTable: "Payments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Members_MemberId",
                table: "Payments",
                column: "MemberId",
                principalTable: "Members",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Plots_PlotId",
                table: "Payments",
                column: "PlotId",
                principalTable: "Plots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PlotOwnershipHistories_Plots_PlotId",
                table: "PlotOwnershipHistories",
                column: "PlotId",
                principalTable: "Plots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PlotOwnerships_Members_MemberId",
                table: "PlotOwnerships",
                column: "MemberId",
                principalTable: "Members",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PlotOwnerships_Plots_PlotId",
                table: "PlotOwnerships",
                column: "PlotId",
                principalTable: "Plots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Plots_MemberElectricityMeters_MemberElectricityMeterId",
                table: "Plots",
                column: "MemberElectricityMeterId",
                principalTable: "MemberElectricityMeters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
