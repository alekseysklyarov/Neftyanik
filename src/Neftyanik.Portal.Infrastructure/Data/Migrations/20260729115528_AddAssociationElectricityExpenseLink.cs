using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Neftyanik.Portal.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAssociationElectricityExpenseLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AssociationElectricityReadingId",
                table: "Expenses",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_AssociationElectricityReadingId",
                table: "Expenses",
                column: "AssociationElectricityReadingId",
                unique: true,
                filter: "[AssociationElectricityReadingId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_AssociationElectricityReadings_AssociationElectricityReadingId",
                table: "Expenses",
                column: "AssociationElectricityReadingId",
                principalTable: "AssociationElectricityReadings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_AssociationElectricityReadings_AssociationElectricityReadingId",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_AssociationElectricityReadingId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "AssociationElectricityReadingId",
                table: "Expenses");
        }
    }
}
