using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Neftyanik.Portal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixFinancialOwnershipAndExpenseAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Expenses_AssociationId_AssociationElectricityReadingId",
                table: "Expenses");

            migrationBuilder.AddColumn<int>(
                name: "FundingSource",
                table: "Expenses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PaymentMethod",
                table: "Expenses",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "IsMembershipFee",
                table: "ChargeTypes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MemberId",
                table: "Charges",
                type: "int",
                nullable: true);

            // Preserve the previous cash treatment until an accountant classifies historical expenses.
            migrationBuilder.Sql("UPDATE Expenses SET FundingSource = 2 WHERE AssociationElectricityReadingId IS NOT NULL");
            if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql("""
                    UPDATE c SET MemberId = COALESCE(
                        (SELECT TOP (1) m.MemberId FROM MemberElectricityReadings r
                         JOIN MemberElectricityMeters m ON m.Id = r.MemberElectricityMeterId AND m.AssociationId = r.AssociationId
                         WHERE r.ChargeId = c.Id AND r.AssociationId = c.AssociationId),
                        (SELECT MIN(o.MemberId) FROM PlotOwnerships o
                         WHERE o.PlotId = c.PlotId AND o.AssociationId = c.AssociationId
                           AND (o.ValidFrom IS NULL OR o.ValidFrom <= c.ChargeDate)
                           AND (o.ValidTo IS NULL OR o.ValidTo >= c.ChargeDate)
                         HAVING COUNT(DISTINCT o.MemberId) = 1),
                        (SELECT MIN(p.MemberId) FROM PaymentAllocations a
                         JOIN Payments p ON p.Id = a.PaymentId AND p.AssociationId = a.AssociationId
                         WHERE a.ChargeId = c.Id AND a.AssociationId = c.AssociationId
                         HAVING COUNT(DISTINCT p.MemberId) = 1),
                        (SELECT MIN(o.MemberId) FROM PlotOwnerships o
                         WHERE o.PlotId = c.PlotId AND o.AssociationId = c.AssociationId
                         HAVING COUNT(DISTINCT o.MemberId) = 1))
                    FROM Charges c WHERE c.MemberId IS NULL;
                    """);
            }

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_AssociationId_AssociationElectricityReadingId",
                table: "Expenses",
                columns: new[] { "AssociationId", "AssociationElectricityReadingId" },
                filter: "[AssociationElectricityReadingId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Charges_AssociationId_MemberId",
                table: "Charges",
                columns: new[] { "AssociationId", "MemberId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Charges_Members_AssociationId_MemberId",
                table: "Charges",
                columns: new[] { "AssociationId", "MemberId" },
                principalTable: "Members",
                principalColumns: new[] { "AssociationId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Charges_Members_AssociationId_MemberId",
                table: "Charges");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_AssociationId_AssociationElectricityReadingId",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Charges_AssociationId_MemberId",
                table: "Charges");

            migrationBuilder.DropColumn(
                name: "FundingSource",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "IsMembershipFee",
                table: "ChargeTypes");

            migrationBuilder.DropColumn(
                name: "MemberId",
                table: "Charges");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_AssociationId_AssociationElectricityReadingId",
                table: "Expenses",
                columns: new[] { "AssociationId", "AssociationElectricityReadingId" },
                unique: true,
                filter: "[AssociationElectricityReadingId] IS NOT NULL");
        }
    }
}
