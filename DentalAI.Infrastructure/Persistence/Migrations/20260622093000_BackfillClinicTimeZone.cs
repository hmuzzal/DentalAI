using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackfillClinicTimeZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.Clinics', 'TimeZone') IS NULL
                    ALTER TABLE [dbo].[Clinics] ADD [TimeZone] nvarchar(100) NOT NULL CONSTRAINT [DF_Clinics_TimeZone] DEFAULT N'UTC';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.Clinics', 'TimeZone') IS NOT NULL
                BEGIN
                    DECLARE @constraintName sysname;
                    SELECT @constraintName = dc.name
                    FROM sys.default_constraints AS dc
                    INNER JOIN sys.columns AS c
                        ON c.object_id = dc.parent_object_id
                       AND c.column_id = dc.parent_column_id
                    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.Clinics')
                      AND c.name = N'TimeZone';

                    IF @constraintName IS NOT NULL
                        EXEC(N'ALTER TABLE [dbo].[Clinics] DROP CONSTRAINT [' + @constraintName + N']');

                    ALTER TABLE [dbo].[Clinics] DROP COLUMN [TimeZone];
                END
                """);
        }
    }
}
