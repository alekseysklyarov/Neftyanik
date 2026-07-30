using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Neftyanik.Portal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateMemberElectricityMeterPlotRelationship : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[MemberElectricityMeterPlots]', N'U') IS NOT NULL
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM [dbo].[MemberElectricityMeterPlots]
                        GROUP BY [PlotId]
                        HAVING COUNT(*) > 1)
                    BEGIN
                        THROW 51000, 'Cannot migrate MemberElectricityMeterPlots to Plots.MemberElectricityMeterId because one or more plots are linked to multiple meters. Resolve the conflicting rows before applying this migration.', 1;
                    END
                END
                """);

            migrationBuilder.AddColumn<int>(
                name: "MemberElectricityMeterId",
                table: "Plots",
                type: "int",
                nullable: true);

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[MemberElectricityMeterPlots]', N'U') IS NOT NULL
                BEGIN
                    UPDATE [p]
                    SET [p].[MemberElectricityMeterId] = [mp].[MemberElectricityMeterId]
                    FROM [dbo].[Plots] AS [p]
                    INNER JOIN [dbo].[MemberElectricityMeterPlots] AS [mp] ON [mp].[PlotId] = [p].[Id]
                    WHERE [p].[MemberElectricityMeterId] IS NULL;
                END
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Plots_MemberElectricityMeterId",
                table: "Plots",
                column: "MemberElectricityMeterId");

            migrationBuilder.AddForeignKey(
                name: "FK_Plots_MemberElectricityMeters_MemberElectricityMeterId",
                table: "Plots",
                column: "MemberElectricityMeterId",
                principalTable: "MemberElectricityMeters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[MemberElectricityMeterPlots]', N'U') IS NOT NULL
                BEGIN
                    DROP TABLE [dbo].[MemberElectricityMeterPlots];
                END
                """);

            migrationBuilder.Sql(
                """
                IF COL_LENGTH(N'[dbo].[MemberElectricityReadings]', N'Consumption') IS NOT NULL
                BEGIN
                    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE [name] = N'CK_MemberElectricityReadings_Consumption_NonNegative')
                    BEGIN
                        ALTER TABLE [dbo].[MemberElectricityReadings] DROP CONSTRAINT [CK_MemberElectricityReadings_Consumption_NonNegative];
                    END

                    ALTER TABLE [dbo].[MemberElectricityReadings] DROP COLUMN [Consumption];
                END
                """);

            migrationBuilder.Sql(
                """
                IF COL_LENGTH(N'[dbo].[MemberElectricityReadings]', N'PreviousReading') IS NOT NULL
                BEGIN
                    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE [name] = N'CK_MemberElectricityReadings_PreviousReading_NonNegative')
                    BEGIN
                        ALTER TABLE [dbo].[MemberElectricityReadings] DROP CONSTRAINT [CK_MemberElectricityReadings_PreviousReading_NonNegative];
                    END

                    ALTER TABLE [dbo].[MemberElectricityReadings] DROP COLUMN [PreviousReading];
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Consumption",
                table: "MemberElectricityReadings",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PreviousReading",
                table: "MemberElectricityReadings",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.Sql(
                """
                ;WITH [OrderedReadings] AS (
                    SELECT
                        [Id],
                        [CurrentReading],
                        [IsInitialReading],
                        LAG([CurrentReading]) OVER (PARTITION BY [MemberElectricityMeterId] ORDER BY [ReadingDate], [Id]) AS [PreviousReadingValue]
                    FROM [dbo].[MemberElectricityReadings])
                UPDATE [r]
                SET
                    [r].[PreviousReading] = CASE
                        WHEN [o].[IsInitialReading] = 1 THEN NULL
                        ELSE [o].[PreviousReadingValue]
                    END,
                    [r].[Consumption] = CASE
                        WHEN [o].[IsInitialReading] = 1 OR [o].[PreviousReadingValue] IS NULL THEN NULL
                        ELSE [r].[CurrentReading] - [o].[PreviousReadingValue]
                    END
                FROM [dbo].[MemberElectricityReadings] AS [r]
                INNER JOIN [OrderedReadings] AS [o] ON [o].[Id] = [r].[Id];
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_MemberElectricityReadings_Consumption_NonNegative",
                table: "MemberElectricityReadings",
                sql: "[Consumption] IS NULL OR [Consumption] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MemberElectricityReadings_PreviousReading_NonNegative",
                table: "MemberElectricityReadings",
                sql: "[PreviousReading] IS NULL OR [PreviousReading] >= 0");

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

            migrationBuilder.CreateIndex(
                name: "IX_MemberElectricityMeterPlots_PlotId",
                table: "MemberElectricityMeterPlots",
                column: "PlotId");

            migrationBuilder.Sql(
                """
                INSERT INTO [dbo].[MemberElectricityMeterPlots] ([MemberElectricityMeterId], [PlotId])
                SELECT [MemberElectricityMeterId], [Id]
                FROM [dbo].[Plots]
                WHERE [MemberElectricityMeterId] IS NOT NULL;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Plots_MemberElectricityMeters_MemberElectricityMeterId",
                table: "Plots");

            migrationBuilder.DropIndex(
                name: "IX_Plots_MemberElectricityMeterId",
                table: "Plots");

            migrationBuilder.DropColumn(
                name: "MemberElectricityMeterId",
                table: "Plots");
        }
    }
}
