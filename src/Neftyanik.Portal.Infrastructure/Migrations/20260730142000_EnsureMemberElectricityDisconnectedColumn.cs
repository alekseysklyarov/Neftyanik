using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Neftyanik.Portal.Infrastructure.Data;

#nullable disable

namespace Neftyanik.Portal.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260730142000_EnsureMemberElectricityDisconnectedColumn")]
public class EnsureMemberElectricityDisconnectedColumn : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF COL_LENGTH('Members', 'IsElectricityDisconnected') IS NULL
            BEGIN
                ALTER TABLE [Members]
                ADD [IsElectricityDisconnected] bit NOT NULL
                    CONSTRAINT [DF_Members_IsElectricityDisconnected] DEFAULT CAST(0 AS bit);
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF COL_LENGTH('Members', 'IsElectricityDisconnected') IS NOT NULL
            BEGIN
                DECLARE @constraintName sysname;

                SELECT @constraintName = dc.name
                FROM sys.default_constraints dc
                INNER JOIN sys.columns c ON c.default_object_id = dc.object_id
                INNER JOIN sys.tables t ON t.object_id = c.object_id
                WHERE t.name = 'Members'
                  AND c.name = 'IsElectricityDisconnected';

                IF @constraintName IS NOT NULL
                BEGIN
                    EXEC('ALTER TABLE [Members] DROP CONSTRAINT [' + @constraintName + ']');
                END

                ALTER TABLE [Members] DROP COLUMN [IsElectricityDisconnected];
            END
            """);
    }
}
