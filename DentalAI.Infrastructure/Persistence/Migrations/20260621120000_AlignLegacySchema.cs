using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalAI.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// InitialCreate built Clinics, Doctors, Users, Patients and Appointments in an older shape than the model
    /// (e.g. Appointments.PatientName and ScheduledStartUtc instead of PatientId and StartTime), and later migrations
    /// assume the current shape. These steps used to run from Program.cs on every startup (AlignLegacy*Schema).
    /// Each table is aligned only when it still has the old shape, so this is a no-op on databases that the startup
    /// patches already aligned, which apply it out of order because their history is past this point.
    /// A table's decision is made once and carried to its later batches in SESSION_CONTEXT, because its first batch
    /// changes the columns the decision looks at. Statements naming a column that may not exist run through EXEC, so
    /// the batch still compiles where the column is absent.
    /// </summary>
    public partial class AlignLegacySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            AlignClinics(migrationBuilder);
            AlignDoctors(migrationBuilder);
            AlignUsers(migrationBuilder);
            AlignPatients(migrationBuilder);
            AlignAppointments(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // One-way: the old columns' data was moved into the new ones and the old columns dropped.
        }

        private static void AlignClinics(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'dbo.Clinics') IS NOT NULL AND COL_LENGTH('dbo.Clinics', 'TimeZone') IS NULL
                    ALTER TABLE [dbo].[Clinics] ADD [TimeZone] nvarchar(100) NOT NULL CONSTRAINT [DF_Clinics_TimeZone] DEFAULT N'UTC';
                """);
        }

        private static void AlignDoctors(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DECLARE @alignDoctors bit = CASE WHEN OBJECT_ID(N'dbo.Doctors') IS NOT NULL
                    AND COL_LENGTH('dbo.Doctors', 'FullName') IS NOT NULL
                    AND COL_LENGTH('dbo.Doctors', 'Specialization') IS NOT NULL
                    AND (COL_LENGTH('dbo.Doctors', 'Email') IS NULL
                        OR COL_LENGTH('dbo.Doctors', 'FirstName') IS NULL
                        OR COL_LENGTH('dbo.Doctors', 'LastName') IS NULL
                        OR COL_LENGTH('dbo.Doctors', 'Phone') IS NULL
                        OR COL_LENGTH('dbo.Doctors', 'Specialty') IS NULL)
                    THEN 1 ELSE 0 END;
                EXEC sp_set_session_context N'DentalAI.AlignDoctors', @alignDoctors;

                IF @alignDoctors = 1
                BEGIN
                    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Doctors_ClinicId_FullName' AND object_id = OBJECT_ID(N'dbo.Doctors'))
                        DROP INDEX [IX_Doctors_ClinicId_FullName] ON [dbo].[Doctors];

                    IF COL_LENGTH('dbo.Doctors', 'Specialization') IS NOT NULL AND COL_LENGTH('dbo.Doctors', 'Specialty') IS NULL
                        EXEC sp_rename 'dbo.Doctors.Specialization', 'Specialty', 'COLUMN';

                    IF COL_LENGTH('dbo.Doctors', 'Email') IS NULL
                        ALTER TABLE [dbo].[Doctors] ADD [Email] nvarchar(200) NOT NULL CONSTRAINT [DF_Doctors_Email] DEFAULT N'';

                    IF COL_LENGTH('dbo.Doctors', 'FirstName') IS NULL
                        ALTER TABLE [dbo].[Doctors] ADD [FirstName] nvarchar(100) NOT NULL CONSTRAINT [DF_Doctors_FirstName] DEFAULT N'';

                    IF COL_LENGTH('dbo.Doctors', 'LastName') IS NULL
                        ALTER TABLE [dbo].[Doctors] ADD [LastName] nvarchar(100) NOT NULL CONSTRAINT [DF_Doctors_LastName] DEFAULT N'';

                    IF COL_LENGTH('dbo.Doctors', 'Phone') IS NULL
                        ALTER TABLE [dbo].[Doctors] ADD [Phone] nvarchar(30) NOT NULL CONSTRAINT [DF_Doctors_Phone] DEFAULT N'';
                END
                """);

            migrationBuilder.Sql(
                """
                IF CAST(SESSION_CONTEXT(N'DentalAI.AlignDoctors') AS bit) = 1
                BEGIN
                    IF COL_LENGTH('dbo.Doctors', 'FullName') IS NOT NULL
                        EXEC(N'
                        UPDATE [dbo].[Doctors]
                        SET
                            [FirstName] = CASE
                                WHEN LTRIM(RTRIM([FullName])) = N'''' THEN N''''
                                WHEN CHARINDEX(N'' '', LTRIM(RTRIM([FullName]))) > 0 THEN LEFT(LTRIM(RTRIM([FullName])), CHARINDEX(N'' '', LTRIM(RTRIM([FullName]))) - 1)
                                ELSE LTRIM(RTRIM([FullName]))
                            END,
                            [LastName] = CASE
                                WHEN CHARINDEX(N'' '', LTRIM(RTRIM([FullName]))) > 0 THEN LTRIM(SUBSTRING(LTRIM(RTRIM([FullName])), CHARINDEX(N'' '', LTRIM(RTRIM([FullName]))) + 1, LEN(LTRIM(RTRIM([FullName])))))
                                ELSE N''''
                            END
                        WHERE ISNULL([FirstName], N'''') = N'''' AND ISNULL([LastName], N'''') = N'''';

                        ALTER TABLE [dbo].[Doctors] DROP COLUMN [FullName];');

                    IF COL_LENGTH('dbo.Doctors', 'IsActive') IS NOT NULL
                        EXEC(N'ALTER TABLE [dbo].[Doctors] DROP COLUMN [IsActive];');

                    IF COL_LENGTH('dbo.Doctors', 'WorkDayEnd') IS NOT NULL
                        EXEC(N'ALTER TABLE [dbo].[Doctors] DROP COLUMN [WorkDayEnd];');

                    IF COL_LENGTH('dbo.Doctors', 'WorkDayStart') IS NOT NULL
                        EXEC(N'ALTER TABLE [dbo].[Doctors] DROP COLUMN [WorkDayStart];');

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Doctors_ClinicId' AND object_id = OBJECT_ID(N'dbo.Doctors'))
                        CREATE INDEX [IX_Doctors_ClinicId] ON [dbo].[Doctors] ([ClinicId]);

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Doctors_ClinicId_LastName_FirstName' AND object_id = OBJECT_ID(N'dbo.Doctors'))
                        CREATE INDEX [IX_Doctors_ClinicId_LastName_FirstName] ON [dbo].[Doctors] ([ClinicId], [LastName], [FirstName]);
                END

                EXEC sp_set_session_context N'DentalAI.AlignDoctors', NULL;
                """);
        }

        private static void AlignUsers(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'dbo.Users') IS NOT NULL
                BEGIN
                    IF COL_LENGTH('dbo.Users', 'FirstName') IS NULL
                        ALTER TABLE [dbo].[Users] ADD [FirstName] nvarchar(100) NOT NULL CONSTRAINT [DF_Users_FirstName] DEFAULT N'';

                    IF COL_LENGTH('dbo.Users', 'LastName') IS NULL
                        ALTER TABLE [dbo].[Users] ADD [LastName] nvarchar(100) NOT NULL CONSTRAINT [DF_Users_LastName] DEFAULT N'';

                    IF COL_LENGTH('dbo.Users', 'RefreshToken') IS NULL
                        ALTER TABLE [dbo].[Users] ADD [RefreshToken] nvarchar(200) NULL;

                    IF COL_LENGTH('dbo.Users', 'RefreshTokenExpiry') IS NULL
                        ALTER TABLE [dbo].[Users] ADD [RefreshTokenExpiry] datetime2 NULL;
                END
                """);

            migrationBuilder.Sql(
                """
                IF COL_LENGTH('dbo.Users', 'FullName') IS NOT NULL
                    EXEC(N'
                    UPDATE [dbo].[Users]
                    SET
                        [FirstName] = CASE
                            WHEN LTRIM(RTRIM([FullName])) = N'''' THEN N''''
                            WHEN CHARINDEX(N'' '', LTRIM(RTRIM([FullName]))) > 0 THEN LEFT(LTRIM(RTRIM([FullName])), CHARINDEX(N'' '', LTRIM(RTRIM([FullName]))) - 1)
                            ELSE LTRIM(RTRIM([FullName]))
                        END,
                        [LastName] = CASE
                            WHEN CHARINDEX(N'' '', LTRIM(RTRIM([FullName]))) > 0 THEN LTRIM(SUBSTRING(LTRIM(RTRIM([FullName])), CHARINDEX(N'' '', LTRIM(RTRIM([FullName]))) + 1, LEN(LTRIM(RTRIM([FullName])))))
                            ELSE N''''
                        END
                    WHERE ISNULL([FirstName], N'''') = N'''' AND ISNULL([LastName], N'''') = N'''';

                    ALTER TABLE [dbo].[Users] DROP COLUMN [FullName];');

                -- Platform admins have no clinic.
                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Users') AND name = 'ClinicId' AND is_nullable = 0)
                    ALTER TABLE [dbo].[Users] ALTER COLUMN [ClinicId] uniqueidentifier NULL;
                """);
        }

        private static void AlignPatients(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DECLARE @alignPatients bit = CASE WHEN OBJECT_ID(N'dbo.Patients') IS NOT NULL
                    AND (COL_LENGTH('dbo.Patients', 'FirstName') IS NULL
                        OR COL_LENGTH('dbo.Patients', 'LastName') IS NULL
                        OR COL_LENGTH('dbo.Patients', 'Phone') IS NULL
                        OR COL_LENGTH('dbo.Patients', 'Email') IS NULL
                        OR COL_LENGTH('dbo.Patients', 'DateOfBirth') IS NULL
                        OR COL_LENGTH('dbo.Patients', 'Gender') IS NULL
                        OR COL_LENGTH('dbo.Patients', 'Notes') IS NULL)
                    THEN 1 ELSE 0 END;
                EXEC sp_set_session_context N'DentalAI.AlignPatients', @alignPatients;

                IF @alignPatients = 1
                BEGIN
                    IF COL_LENGTH('dbo.Patients', 'FirstName') IS NULL
                        ALTER TABLE [dbo].[Patients] ADD [FirstName] nvarchar(100) NOT NULL CONSTRAINT [DF_Patients_FirstName] DEFAULT N'';

                    IF COL_LENGTH('dbo.Patients', 'LastName') IS NULL
                        ALTER TABLE [dbo].[Patients] ADD [LastName] nvarchar(100) NOT NULL CONSTRAINT [DF_Patients_LastName] DEFAULT N'';

                    IF COL_LENGTH('dbo.Patients', 'Phone') IS NULL
                        ALTER TABLE [dbo].[Patients] ADD [Phone] nvarchar(30) NOT NULL CONSTRAINT [DF_Patients_Phone] DEFAULT N'';

                    IF COL_LENGTH('dbo.Patients', 'Email') IS NULL
                        ALTER TABLE [dbo].[Patients] ADD [Email] nvarchar(200) NULL;

                    IF COL_LENGTH('dbo.Patients', 'DateOfBirth') IS NULL
                        ALTER TABLE [dbo].[Patients] ADD [DateOfBirth] date NULL;

                    IF COL_LENGTH('dbo.Patients', 'Gender') IS NULL
                        ALTER TABLE [dbo].[Patients] ADD [Gender] nvarchar(30) NULL;

                    IF COL_LENGTH('dbo.Patients', 'Notes') IS NULL
                        ALTER TABLE [dbo].[Patients] ADD [Notes] nvarchar(2000) NULL;
                END
                """);

            migrationBuilder.Sql(
                """
                IF CAST(SESSION_CONTEXT(N'DentalAI.AlignPatients') AS bit) = 1
                BEGIN
                    IF COL_LENGTH('dbo.Patients', 'Name') IS NOT NULL
                        EXEC(N'
                        UPDATE [dbo].[Patients]
                        SET
                            [FirstName] = CASE
                                WHEN LTRIM(RTRIM([Name])) = N'''' THEN N''Legacy''
                                WHEN CHARINDEX(N'' '', LTRIM(RTRIM([Name]))) > 0 THEN LEFT(LTRIM(RTRIM([Name])), CHARINDEX(N'' '', LTRIM(RTRIM([Name]))) - 1)
                                ELSE LTRIM(RTRIM([Name]))
                            END,
                            [LastName] = CASE
                                WHEN CHARINDEX(N'' '', LTRIM(RTRIM([Name]))) > 0 THEN LTRIM(SUBSTRING(LTRIM(RTRIM([Name])), CHARINDEX(N'' '', LTRIM(RTRIM([Name]))) + 1, LEN(LTRIM(RTRIM([Name])))))
                                ELSE N''Patient''
                            END
                        WHERE ISNULL([FirstName], N'''') = N'''' AND ISNULL([LastName], N'''') = N'''';');

                    IF COL_LENGTH('dbo.Patients', 'FullName') IS NOT NULL
                        EXEC(N'
                        UPDATE [dbo].[Patients]
                        SET
                            [FirstName] = CASE
                                WHEN LTRIM(RTRIM([FullName])) = N'''' THEN N''Legacy''
                                WHEN CHARINDEX(N'' '', LTRIM(RTRIM([FullName]))) > 0 THEN LEFT(LTRIM(RTRIM([FullName])), CHARINDEX(N'' '', LTRIM(RTRIM([FullName]))) - 1)
                                ELSE LTRIM(RTRIM([FullName]))
                            END,
                            [LastName] = CASE
                                WHEN CHARINDEX(N'' '', LTRIM(RTRIM([FullName]))) > 0 THEN LTRIM(SUBSTRING(LTRIM(RTRIM([FullName])), CHARINDEX(N'' '', LTRIM(RTRIM([FullName]))) + 1, LEN(LTRIM(RTRIM([FullName])))))
                                ELSE N''Patient''
                            END
                        WHERE ISNULL([FirstName], N'''') = N'''' AND ISNULL([LastName], N'''') = N'''';');

                    UPDATE [dbo].[Patients]
                    SET [FirstName] = N'Legacy'
                    WHERE ISNULL([FirstName], N'') = N'';

                    UPDATE [dbo].[Patients]
                    SET [LastName] = N'Patient'
                    WHERE ISNULL([LastName], N'') = N'';
                END

                EXEC sp_set_session_context N'DentalAI.AlignPatients', NULL;
                """);
        }

        private static void AlignAppointments(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DECLARE @alignAppointments bit = CASE WHEN OBJECT_ID(N'dbo.Appointments') IS NOT NULL
                    AND (COL_LENGTH('dbo.Appointments', 'PatientName') IS NOT NULL
                        OR COL_LENGTH('dbo.Appointments', 'StartAt') IS NOT NULL
                        OR COL_LENGTH('dbo.Appointments', 'EndAt') IS NOT NULL
                        OR COL_LENGTH('dbo.Appointments', 'ScheduledStartUtc') IS NOT NULL
                        OR COL_LENGTH('dbo.Appointments', 'ScheduledEndUtc') IS NOT NULL
                        OR COL_LENGTH('dbo.Appointments', 'PatientId') IS NULL
                        OR COL_LENGTH('dbo.Appointments', 'StartTime') IS NULL
                        OR COL_LENGTH('dbo.Appointments', 'EndTime') IS NULL)
                    THEN 1 ELSE 0 END;
                EXEC sp_set_session_context N'DentalAI.AlignAppointments', @alignAppointments;

                IF @alignAppointments = 1
                BEGIN
                    IF COL_LENGTH('dbo.Appointments', 'StartTime') IS NULL AND COL_LENGTH('dbo.Appointments', 'StartAt') IS NOT NULL
                        EXEC sp_rename 'dbo.Appointments.StartAt', 'StartTime', 'COLUMN';

                    IF COL_LENGTH('dbo.Appointments', 'EndTime') IS NULL AND COL_LENGTH('dbo.Appointments', 'EndAt') IS NOT NULL
                        EXEC sp_rename 'dbo.Appointments.EndAt', 'EndTime', 'COLUMN';

                    IF COL_LENGTH('dbo.Appointments', 'StartTime') IS NULL
                        ALTER TABLE [dbo].[Appointments] ADD [StartTime] datetime2 NOT NULL CONSTRAINT [DF_Appointments_StartTime] DEFAULT '0001-01-01T00:00:00.0000000';

                    IF COL_LENGTH('dbo.Appointments', 'EndTime') IS NULL
                        ALTER TABLE [dbo].[Appointments] ADD [EndTime] datetime2 NOT NULL CONSTRAINT [DF_Appointments_EndTime] DEFAULT '0001-01-01T00:00:00.0000000';

                    IF COL_LENGTH('dbo.Appointments', 'Notes') IS NULL
                        ALTER TABLE [dbo].[Appointments] ADD [Notes] nvarchar(2000) NULL;

                    IF COL_LENGTH('dbo.Appointments', 'PatientId') IS NULL
                        ALTER TABLE [dbo].[Appointments] ADD [PatientId] uniqueidentifier NULL;
                END
                """);

            migrationBuilder.Sql(
                """
                IF CAST(SESSION_CONTEXT(N'DentalAI.AlignAppointments') AS bit) = 1
                BEGIN
                    IF COL_LENGTH('dbo.Appointments', 'ScheduledStartUtc') IS NOT NULL
                        EXEC(N'UPDATE [dbo].[Appointments] SET [StartTime] = [ScheduledStartUtc] WHERE [StartTime] = ''0001-01-01T00:00:00.0000000'';');

                    IF COL_LENGTH('dbo.Appointments', 'ScheduledEndUtc') IS NOT NULL
                        EXEC(N'UPDATE [dbo].[Appointments] SET [EndTime] = [ScheduledEndUtc] WHERE [EndTime] = ''0001-01-01T00:00:00.0000000'';');

                    IF COL_LENGTH('dbo.Appointments', 'PatientName') IS NOT NULL
                        EXEC(N'
                        INSERT INTO [dbo].[Patients] ([Id], [ClinicId], [FirstName], [LastName], [Phone], [Email], [DateOfBirth], [Gender], [Notes], [CreatedDate], [CreatedBy], [UpdatedDate], [UpdatedBy], [IsDeleted])
                        SELECT NEWID(),
                               source.[ClinicId],
                               CASE
                                   WHEN LTRIM(RTRIM(source.[PatientName])) = N'''' THEN N''Legacy''
                                   WHEN CHARINDEX(N'' '', LTRIM(RTRIM(source.[PatientName]))) > 0 THEN LEFT(LTRIM(RTRIM(source.[PatientName])), CHARINDEX(N'' '', LTRIM(RTRIM(source.[PatientName]))) - 1)
                                   ELSE LTRIM(RTRIM(source.[PatientName]))
                               END,
                               CASE
                                   WHEN CHARINDEX(N'' '', LTRIM(RTRIM(source.[PatientName]))) > 0 THEN LTRIM(SUBSTRING(LTRIM(RTRIM(source.[PatientName])), CHARINDEX(N'' '', LTRIM(RTRIM(source.[PatientName]))) + 1, LEN(LTRIM(RTRIM(source.[PatientName])))))
                                   ELSE N''Patient''
                               END,
                               N'''',
                               NULL,
                               NULL,
                               NULL,
                               NULL,
                               SYSUTCDATETIME(),
                               N''migration'',
                               NULL,
                               NULL,
                               0
                        FROM (
                            SELECT DISTINCT [ClinicId], [PatientName]
                            FROM [dbo].[Appointments]
                            WHERE ISNULL(LTRIM(RTRIM([PatientName])), N'''') <> N''''
                        ) AS source
                        WHERE NOT EXISTS (
                            SELECT 1
                            FROM [dbo].[Patients] AS p
                            WHERE p.[ClinicId] = source.[ClinicId]
                              AND p.[FirstName] = CASE
                                  WHEN LTRIM(RTRIM(source.[PatientName])) = N'''' THEN N''Legacy''
                                  WHEN CHARINDEX(N'' '', LTRIM(RTRIM(source.[PatientName]))) > 0 THEN LEFT(LTRIM(RTRIM(source.[PatientName])), CHARINDEX(N'' '', LTRIM(RTRIM(source.[PatientName]))) - 1)
                                  ELSE LTRIM(RTRIM(source.[PatientName]))
                              END
                              AND p.[LastName] = CASE
                                  WHEN CHARINDEX(N'' '', LTRIM(RTRIM(source.[PatientName]))) > 0 THEN LTRIM(SUBSTRING(LTRIM(RTRIM(source.[PatientName])), CHARINDEX(N'' '', LTRIM(RTRIM(source.[PatientName]))) + 1, LEN(LTRIM(RTRIM(source.[PatientName])))))
                                  ELSE N''Patient''
                              END
                              AND p.[IsDeleted] = 0
                        );

                        UPDATE appointment
                        SET [PatientId] = patient.[Id]
                        FROM [dbo].[Appointments] AS appointment
                        INNER JOIN [dbo].[Patients] AS patient
                            ON patient.[ClinicId] = appointment.[ClinicId]
                           AND patient.[FirstName] = CASE
                               WHEN LTRIM(RTRIM(appointment.[PatientName])) = N'''' THEN N''Legacy''
                               WHEN CHARINDEX(N'' '', LTRIM(RTRIM(appointment.[PatientName]))) > 0 THEN LEFT(LTRIM(RTRIM(appointment.[PatientName])), CHARINDEX(N'' '', LTRIM(RTRIM(appointment.[PatientName]))) - 1)
                               ELSE LTRIM(RTRIM(appointment.[PatientName]))
                           END
                           AND patient.[LastName] = CASE
                               WHEN CHARINDEX(N'' '', LTRIM(RTRIM(appointment.[PatientName]))) > 0 THEN LTRIM(SUBSTRING(LTRIM(RTRIM(appointment.[PatientName])), CHARINDEX(N'' '', LTRIM(RTRIM(appointment.[PatientName]))) + 1, LEN(LTRIM(RTRIM(appointment.[PatientName])))))
                               ELSE N''Patient''
                           END
                           AND patient.[IsDeleted] = 0
                        WHERE appointment.[PatientId] IS NULL;');

                    IF EXISTS (SELECT 1 FROM [dbo].[Appointments] WHERE [PatientId] IS NULL)
                    BEGIN
                        DECLARE @fallbackClinicId uniqueidentifier;
                        DECLARE clinic_cursor CURSOR LOCAL STATIC READ_ONLY FOR
                            SELECT DISTINCT [ClinicId] FROM [dbo].[Appointments] WHERE [PatientId] IS NULL;

                        OPEN clinic_cursor;
                        FETCH NEXT FROM clinic_cursor INTO @fallbackClinicId;
                        WHILE @@FETCH_STATUS = 0
                        BEGIN
                            DECLARE @fallbackPatientId uniqueidentifier = (
                                SELECT TOP 1 [Id]
                                FROM [dbo].[Patients]
                                WHERE [ClinicId] = @fallbackClinicId
                                  AND [FirstName] = N'Legacy'
                                  AND [LastName] = N'Patient'
                                  AND [IsDeleted] = 0
                            );

                            IF @fallbackPatientId IS NULL
                            BEGIN
                                SET @fallbackPatientId = NEWID();
                                INSERT INTO [dbo].[Patients] ([Id], [ClinicId], [FirstName], [LastName], [Phone], [Email], [DateOfBirth], [Gender], [Notes], [CreatedDate], [CreatedBy], [UpdatedDate], [UpdatedBy], [IsDeleted])
                                VALUES (@fallbackPatientId, @fallbackClinicId, N'Legacy', N'Patient', N'', NULL, NULL, NULL, N'Backfilled during schema alignment.', SYSUTCDATETIME(), N'migration', NULL, NULL, 0);
                            END

                            UPDATE [dbo].[Appointments]
                            SET [PatientId] = @fallbackPatientId
                            WHERE [ClinicId] = @fallbackClinicId
                              AND [PatientId] IS NULL;

                            FETCH NEXT FROM clinic_cursor INTO @fallbackClinicId;
                        END

                        CLOSE clinic_cursor;
                        DEALLOCATE clinic_cursor;
                    END

                    -- Indexes on the legacy columns (e.g. InitialCreate's IX_Appointments_*_ScheduledStartUtc) block
                    -- dropping them, so drop those indexes first.
                    DECLARE @legacyIndexSql nvarchar(max) = N'';
                    SELECT @legacyIndexSql = @legacyIndexSql + N'DROP INDEX ' + QUOTENAME(i.name) + N' ON [dbo].[Appointments];'
                    FROM sys.indexes AS i
                    WHERE i.object_id = OBJECT_ID(N'dbo.Appointments')
                      AND i.is_primary_key = 0
                      AND EXISTS (
                          SELECT 1
                          FROM sys.index_columns AS ic
                          INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                          WHERE ic.object_id = i.object_id
                            AND ic.index_id = i.index_id
                            AND c.name IN (N'PatientName', N'ScheduledStartUtc', N'ScheduledEndUtc'));
                    IF @legacyIndexSql <> N''
                        EXEC sp_executesql @legacyIndexSql;

                    DECLARE @legacyColumn sysname;
                    DECLARE @constraintName sysname;
                    DECLARE @dropSql nvarchar(max);
                    DECLARE legacy_column_cursor CURSOR LOCAL STATIC READ_ONLY FOR
                        SELECT c.name
                        FROM sys.columns AS c
                        WHERE c.object_id = OBJECT_ID(N'dbo.Appointments')
                          AND c.name IN (N'PatientName', N'ScheduledStartUtc', N'ScheduledEndUtc');

                    OPEN legacy_column_cursor;
                    FETCH NEXT FROM legacy_column_cursor INTO @legacyColumn;
                    WHILE @@FETCH_STATUS = 0
                    BEGIN
                        SET @constraintName = NULL;
                        SELECT @constraintName = dc.name
                        FROM sys.default_constraints AS dc
                        INNER JOIN sys.columns AS c
                            ON c.object_id = dc.parent_object_id
                           AND c.column_id = dc.parent_column_id
                        WHERE dc.parent_object_id = OBJECT_ID(N'dbo.Appointments')
                          AND c.name = @legacyColumn;

                        IF @constraintName IS NOT NULL
                        BEGIN
                            SET @dropSql = N'ALTER TABLE [dbo].[Appointments] DROP CONSTRAINT ' + QUOTENAME(@constraintName) + N';';
                            EXEC sp_executesql @dropSql;
                        END

                        SET @dropSql = N'ALTER TABLE [dbo].[Appointments] DROP COLUMN ' + QUOTENAME(@legacyColumn) + N';';
                        EXEC sp_executesql @dropSql;

                        FETCH NEXT FROM legacy_column_cursor INTO @legacyColumn;
                    END

                    CLOSE legacy_column_cursor;
                    DEALLOCATE legacy_column_cursor;
                END
                """);

            migrationBuilder.Sql(
                """
                IF CAST(SESSION_CONTEXT(N'DentalAI.AlignAppointments') AS bit) = 1
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM sys.columns
                        WHERE object_id = OBJECT_ID(N'dbo.Appointments')
                          AND name = 'PatientId'
                          AND is_nullable = 1
                    )
                        ALTER TABLE [dbo].[Appointments] ALTER COLUMN [PatientId] uniqueidentifier NOT NULL;

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Appointments_PatientId' AND object_id = OBJECT_ID(N'dbo.Appointments'))
                        CREATE INDEX [IX_Appointments_PatientId] ON [dbo].[Appointments] ([PatientId]);

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Appointments_DoctorId' AND object_id = OBJECT_ID(N'dbo.Appointments'))
                        CREATE INDEX [IX_Appointments_DoctorId] ON [dbo].[Appointments] ([DoctorId]);

                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Appointments_ClinicId_DoctorId_StartTime' AND object_id = OBJECT_ID(N'dbo.Appointments'))
                        CREATE INDEX [IX_Appointments_ClinicId_DoctorId_StartTime] ON [dbo].[Appointments] ([ClinicId], [DoctorId], [StartTime]);

                    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Appointments_Patients_PatientId')
                        ALTER TABLE [dbo].[Appointments] ADD CONSTRAINT [FK_Appointments_Patients_PatientId]
                        FOREIGN KEY ([PatientId]) REFERENCES [dbo].[Patients]([Id]) ON DELETE NO ACTION;
                END

                EXEC sp_set_session_context N'DentalAI.AlignAppointments', NULL;
                """);
        }
    }
}
