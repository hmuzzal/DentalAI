using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalAI.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// InitialCreate gave Clinics a required, unique Slug column that the current model no longer maps, so
    /// every clinic insert (the seeder, "Add clinic") failed with "Cannot insert NULL into column 'Slug'".
    /// Keep the column and its values (useful later, e.g. patient-portal URLs), but make it optional and
    /// unique only among clinics that have one. Idempotent; a no-op where Slug doesn't exist.
    /// </summary>
    public partial class AllowNullClinicSlug : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.Clinics', 'Slug') IS NOT NULL
                BEGIN
                    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Clinics_Slug' AND object_id = OBJECT_ID(N'dbo.Clinics') AND has_filter = 0)
                        DROP INDEX [IX_Clinics_Slug] ON [dbo].[Clinics];

                    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Clinics') AND name = 'Slug' AND is_nullable = 0)
                    BEGIN
                        DECLARE @slugType nvarchar(200) = (
                            SELECT TYPE_NAME(c.user_type_id)
                                + CASE WHEN c.max_length = -1 THEN N'(max)'
                                       WHEN TYPE_NAME(c.user_type_id) IN (N'nvarchar', N'nchar') THEN N'(' + CAST(c.max_length / 2 AS nvarchar(10)) + N')'
                                       ELSE N'(' + CAST(c.max_length AS nvarchar(10)) + N')' END
                            FROM sys.columns AS c
                            WHERE c.object_id = OBJECT_ID(N'dbo.Clinics') AND c.name = 'Slug');
                        EXEC(N'ALTER TABLE [dbo].[Clinics] ALTER COLUMN [Slug] ' + @slugType + N' NULL;');
                    END

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Clinics_Slug' AND object_id = OBJECT_ID(N'dbo.Clinics'))
                        EXEC(N'CREATE UNIQUE INDEX [IX_Clinics_Slug] ON [dbo].[Clinics] ([Slug]) WHERE [Slug] IS NOT NULL;');
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: restoring NOT NULL would fail once clinics without a slug exist.
        }
    }
}
