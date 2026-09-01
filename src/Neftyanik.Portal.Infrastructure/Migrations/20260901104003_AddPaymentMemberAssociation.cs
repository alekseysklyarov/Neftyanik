using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Neftyanik.Portal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentMemberAssociation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MemberId",
                table: "Payments",
                type: "int",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE payment
                SET payment.MemberId = notification.MemberId
                FROM Payments AS payment
                INNER JOIN PaymentNotifications AS notification ON notification.PaymentId = payment.Id
                WHERE payment.MemberId IS NULL
                  AND notification.MemberId IS NOT NULL;
                """);

            migrationBuilder.Sql(
                """
                UPDATE payment
                SET payment.MemberId = ownership.MemberId
                FROM Payments AS payment
                INNER JOIN PlotOwnerships AS ownership ON ownership.PlotId = payment.PlotId
                WHERE payment.MemberId IS NULL
                  AND payment.PlotId IS NOT NULL
                  AND (ownership.ValidFrom IS NULL OR ownership.ValidFrom <= payment.PaymentDate)
                  AND (ownership.ValidTo IS NULL OR ownership.ValidTo >= payment.PaymentDate)
                  AND 1 = (
                      SELECT COUNT(*)
                      FROM PlotOwnerships AS candidate
                      WHERE candidate.PlotId = payment.PlotId
                        AND (candidate.ValidFrom IS NULL OR candidate.ValidFrom <= payment.PaymentDate)
                        AND (candidate.ValidTo IS NULL OR candidate.ValidTo >= payment.PaymentDate));
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_MemberId",
                table: "Payments",
                column: "MemberId");

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Members_MemberId",
                table: "Payments",
                column: "MemberId",
                principalTable: "Members",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Members_MemberId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_MemberId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "MemberId",
                table: "Payments");
        }
    }
}
