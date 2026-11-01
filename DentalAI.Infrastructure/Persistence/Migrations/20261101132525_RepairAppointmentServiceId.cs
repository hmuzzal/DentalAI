using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalAI.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Restores Appointments.ServiceId (plus its index and FK) on databases where a legacy
    /// startup schema patch in Program.cs dropped it after AddSchedulingEngine had run.
    /// Every step is idempotent, so this is a no-op on healthy databases.
    /// </summary>
    public partial class RepairAppointmentServiceId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Dynamic SQL (EXEC) is used wherever ServiceId is referenced, because the column may not
            // exist when the batch is compiled.
            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.Appointments', 'ServiceId') IS NULL
                    ALTER TABLE [dbo].[Appointments] ADD [ServiceId] uniqueidentifier NULL;
                """);

            migrationBuilder.Sql(
                """
                EXEC(N'
                IF EXISTS (SELECT 1 FROM [dbo].[Appointments] WHERE [ServiceId] IS NULL)
                BEGIN
                    INSERT INTO [dbo].[Services] ([Id], [ClinicId], [Name], [Description], [Price], [DurationMinutes], [IsActive], [CreatedDate], [CreatedBy], [IsDeleted])
                    SELECT NEWID(), a.[ClinicId], N''Legacy appointment'', N''Automatically created while repairing existing appointments.'', 0, 30, 0, SYSUTCDATETIME(), N''migration'', 0
                    FROM [dbo].[Appointments] a
                    WHERE a.[ServiceId] IS NULL
                      AND NOT EXISTS (SELECT 1 FROM [dbo].[Services] s WHERE (s.[ClinicId] = a.[ClinicId] OR (s.[ClinicId] IS NULL AND a.[ClinicId] IS NULL)))
                    GROUP BY a.[ClinicId];

                    UPDATE a
                    SET [ServiceId] = (
                        SELECT TOP (1) s.[Id] FROM [dbo].[Services] s
                        WHERE (s.[ClinicId] = a.[ClinicId] OR (s.[ClinicId] IS NULL AND a.[ClinicId] IS NULL))
                        ORDER BY s.[CreatedDate], s.[Id])
                    FROM [dbo].[Appointments] a
                    WHERE a.[ServiceId] IS NULL;
                END');
                """);

            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1 FROM sys.columns
                    WHERE object_id = OBJECT_ID(N'dbo.Appointments') AND name = 'ServiceId' AND is_nullable = 1)
                    EXEC(N'ALTER TABLE [dbo].[Appointments] ALTER COLUMN [ServiceId] uniqueidentifier NOT NULL;');

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Appointments_ServiceId' AND object_id = OBJECT_ID(N'dbo.Appointments'))
                    EXEC(N'CREATE INDEX [IX_Appointments_ServiceId] ON [dbo].[Appointments] ([ServiceId]);');

                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Appointments_Services_ServiceId')
                    EXEC(N'ALTER TABLE [dbo].[Appointments] ADD CONSTRAINT [FK_Appointments_Services_ServiceId]
                        FOREIGN KEY ([ServiceId]) REFERENCES [dbo].[Services] ([Id]);');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: the column belongs to AddSchedulingEngine, whose Down removes it.
        }
    }
}
