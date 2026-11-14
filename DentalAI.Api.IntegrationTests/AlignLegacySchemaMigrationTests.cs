using DentalAI.Application.Common.Interfaces;
using DentalAI.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace DentalAI.Api.IntegrationTests;

/// <summary>
/// The AlignLegacySchema migration replaced the startup raw-SQL patches. These tests drive IMigrator directly on a
/// scratch SQL Server database: a legacy-shaped database must be upgraded, and a database the old patches already
/// aligned (which applies the migration out of order) must be left untouched.
/// </summary>
public sealed class AlignLegacySchemaMigrationTests : IAsyncLifetime
{
    private const string AlignMigration = "20260621120000_AlignLegacySchema";
    private const string BaselineMigration = "20260621095058_SecureMultiTenantAuthAndPatientsV2";

    private static readonly string[] Tables = ["Clinics", "Doctors", "Users", "Patients", "Appointments"];

    private readonly string _databaseName = $"DentalAI_AlignLegacy_{Guid.NewGuid():N}";

    private static string Server => Environment.GetEnvironmentVariable("DENTALAI_TEST_SQLSERVER") ?? ".";

    private string ConnectionString(string database) =>
        $"Server={Server};Database={database};Trusted_Connection=True;TrustServerCertificate=True";

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(ConnectionString("master"));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF DB_ID(N'{_databaseName}') IS NOT NULL BEGIN " +
            $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
            $"DROP DATABASE [{_databaseName}]; END";
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Migrate_LegacyAppointmentsWithPatientName_CreatesPatientsAndDropsLegacyColumns()
    {
        // Arrange: a database at the V2 baseline, reshaped like the pre-migration legacy schema
        // (Appointments carry PatientName and no patient link).
        await MigrateAsync(BaselineMigration);

        var clinicId = Guid.NewGuid();
        var doctorId = Guid.NewGuid();
        var janeId = Guid.NewGuid();
        var blankId = Guid.NewGuid();
        var start = new DateTime(2026, 3, 2, 14, 0, 0, DateTimeKind.Utc);
        var end = start.AddMinutes(30);

        await ExecuteAsync(
            """
            ALTER TABLE [dbo].[Appointments] ADD [PatientName] nvarchar(150) NULL;
            ALTER TABLE [dbo].[Appointments] DROP CONSTRAINT [FK_Appointments_Patients_PatientId];
            DROP INDEX [IX_Appointments_PatientId] ON [dbo].[Appointments];
            DROP INDEX [IX_Appointments_ClinicId_PatientId_ScheduledStartUtc] ON [dbo].[Appointments];
            ALTER TABLE [dbo].[Appointments] ALTER COLUMN [PatientId] uniqueidentifier NULL;
            """);
        await SeedClinicAndDoctorAsync(clinicId, doctorId);
        await InsertLegacyAppointmentAsync(janeId, clinicId, doctorId, "Jane Doe", start, end);
        await InsertLegacyAppointmentAsync(blankId, clinicId, doctorId, "   ", start.AddHours(2), end.AddHours(2));

        // Act
        await MigrateAsync();

        // Assert
        await using var connection = await OpenAsync();

        var janePatient = await QueryAsync(connection,
            "SELECT [Id], [FirstName], [LastName] FROM [Patients] WHERE [ClinicId] = @c AND [FirstName] = N'Jane' AND [LastName] = N'Doe'",
            ("@c", clinicId));
        var jane = Assert.Single(janePatient);
        var janePatientId = (Guid)jane[0]!;

        var janeAppointment = Assert.Single(await QueryAsync(connection,
            "SELECT [PatientId], [StartTime], [EndTime] FROM [Appointments] WHERE [Id] = @id", ("@id", janeId)));
        Assert.Equal(janePatientId, (Guid)janeAppointment[0]!);
        Assert.Equal(start, (DateTime)janeAppointment[1]!);
        Assert.Equal(end, (DateTime)janeAppointment[2]!);

        var blankAppointment = Assert.Single(await QueryAsync(connection,
            "SELECT a.[PatientId], p.[FirstName], p.[LastName], p.[ClinicId] FROM [Appointments] a " +
            "JOIN [Patients] p ON p.[Id] = a.[PatientId] WHERE a.[Id] = @id", ("@id", blankId)));
        Assert.Equal("Legacy", (string)blankAppointment[1]!);
        Assert.Equal("Patient", (string)blankAppointment[2]!);
        Assert.Equal(clinicId, (Guid)blankAppointment[3]!);
        Assert.NotEqual(janePatientId, (Guid)blankAppointment[0]!);

        foreach (var column in new[] { "PatientName", "ScheduledStartUtc", "ScheduledEndUtc" })
        {
            Assert.False(await ColumnExistsAsync(connection, "Appointments", column), $"{column} should be dropped");
        }

        Assert.Equal(0, await ScalarAsync(connection, "SELECT COUNT(*) FROM [Appointments] WHERE [PatientId] IS NULL"));
        Assert.True(await IsNullableAsync(connection, "Users", "ClinicId"));
        Assert.False(await IsNullableAsync(connection, "Appointments", "PatientId"));
        Assert.True(await ColumnExistsAsync(connection, "Clinics", "TimeZone"));
        Assert.True(await HistoryHasAsync(connection, AlignMigration));
    }

    [Fact]
    public async Task Migrate_InitialCreateShapedAppointments_CopiesScheduledTimesToStartAndEnd()
    {
        // Arrange: rows exactly as the real migration history leaves them at the V2 baseline.
        await MigrateAsync(BaselineMigration);

        var clinicId = Guid.NewGuid();
        var doctorId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();
        var start = new DateTime(2026, 4, 6, 9, 30, 0, DateTimeKind.Utc);
        var end = start.AddMinutes(45);

        await SeedClinicAndDoctorAsync(clinicId, doctorId);
        await ExecuteAsync(
            "INSERT INTO [Patients] ([Id],[ClinicId],[FirstName],[LastName],[Phone],[CreatedDate],[IsDeleted]) " +
            "VALUES (@p,@c,N'Sam',N'Smith',N'+12125550100',SYSUTCDATETIME(),0)",
            ("@p", patientId), ("@c", clinicId));
        await ExecuteAsync(
            "INSERT INTO [Appointments] ([Id],[ClinicId],[DoctorId],[PatientId],[ScheduledStartUtc],[ScheduledEndUtc],[Status],[CreatedDate],[IsDeleted]) " +
            "VALUES (@id,@c,@d,@p,@s,@e,N'Scheduled',SYSUTCDATETIME(),0)",
            ("@id", appointmentId), ("@c", clinicId), ("@d", doctorId), ("@p", patientId), ("@s", start), ("@e", end));

        // Act
        await MigrateAsync();

        // Assert
        await using var connection = await OpenAsync();
        var row = Assert.Single(await QueryAsync(connection,
            "SELECT [PatientId], [StartTime], [EndTime] FROM [Appointments] WHERE [Id] = @id", ("@id", appointmentId)));
        Assert.Equal(patientId, (Guid)row[0]!);
        Assert.Equal(start, (DateTime)row[1]!);
        Assert.Equal(end, (DateTime)row[2]!);
        Assert.False(await ColumnExistsAsync(connection, "Appointments", "ScheduledStartUtc"));
        Assert.False(await ColumnExistsAsync(connection, "Appointments", "ScheduledEndUtc"));
    }

    [Fact]
    public async Task Migrate_DatabaseAlreadyAlignedByStartupPatches_AppliedOutOfOrderIsNoOp()
    {
        // Arrange: a fully migrated database with data, whose history lacks the AlignLegacySchema row,
        // like one the old startup patches aligned before this migration existed.
        await MigrateAsync(BaselineMigration);
        var clinicId = Guid.NewGuid();
        var doctorId = Guid.NewGuid();
        await SeedClinicAndDoctorAsync(clinicId, doctorId);
        await MigrateAsync();

        var patientId = Guid.NewGuid();
        await ExecuteAsync(
            "INSERT INTO [Patients] ([Id],[ClinicId],[FirstName],[LastName],[Phone],[CreatedDate],[IsDeleted]) " +
            "VALUES (@p,@c,N'Ann',N'Lee',N'+12125550101',SYSUTCDATETIME(),0)",
            ("@p", patientId), ("@c", clinicId));
        var serviceId = Guid.NewGuid();
        await ExecuteAsync(
            "INSERT INTO [Services] ([Id],[ClinicId],[Name],[Price],[DurationMinutes],[IsActive],[CreatedDate],[IsDeleted]) " +
            "VALUES (@sv,@c,N'Checkup',50,30,1,SYSUTCDATETIME(),0)",
            ("@sv", serviceId), ("@c", clinicId));
        var start = new DateTime(2026, 5, 4, 10, 0, 0, DateTimeKind.Utc);
        await ExecuteAsync(
            "INSERT INTO [Appointments] ([Id],[ClinicId],[DoctorId],[PatientId],[ServiceId],[StartTime],[EndTime],[Status],[CreatedDate],[IsDeleted]) " +
            "VALUES (@id,@c,@d,@p,@sv,@s,@e,N'Scheduled',SYSUTCDATETIME(),0)",
            ("@sv", serviceId), ("@id", Guid.NewGuid()), ("@c", clinicId), ("@d", doctorId), ("@p", patientId), ("@s", start), ("@e", start.AddMinutes(30)));

        await ExecuteAsync("DELETE FROM [__EFMigrationsHistory] WHERE [MigrationId] = @m", ("@m", AlignMigration));

        string fingerprintBefore;
        Dictionary<string, long> countsBefore;
        await using (var before = await OpenAsync())
        {
            Assert.False(await HistoryHasAsync(before, AlignMigration));
            fingerprintBefore = await SchemaFingerprintAsync(before);
            countsBefore = await RowCountsAsync(before);
        }

        // Act
        await MigrateAsync();

        // Assert
        await using var after = await OpenAsync();
        Assert.True(await HistoryHasAsync(after, AlignMigration));
        Assert.Equal(fingerprintBefore, await SchemaFingerprintAsync(after));
        Assert.Equal(countsBefore, await RowCountsAsync(after));
        Assert.True(countsBefore["Appointments"] >= 1);
    }

    // ---- helpers ----

    private async Task MigrateAsync(string? targetMigration = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString(_databaseName))
            .Options;

        // The migrator never touches the current-user or clock services.
        await using var db = new ApplicationDbContext(options, null!, null!);
        var migrator = ((IInfrastructure<IServiceProvider>)db).Instance.GetService(typeof(IMigrator)) as IMigrator
            ?? throw new InvalidOperationException("IMigrator is not available.");
        await migrator.MigrateAsync(targetMigration);
    }

    private async Task SeedClinicAndDoctorAsync(Guid clinicId, Guid doctorId)
    {
        await ExecuteAsync(
            "INSERT INTO [Clinics] ([Id],[Name],[Slug],[CreatedDate],[IsDeleted]) VALUES (@c,@n,@s,SYSUTCDATETIME(),0)",
            ("@c", clinicId), ("@n", $"Align {clinicId:N}"), ("@s", $"align-{clinicId:N}"));
        await ExecuteAsync(
            "INSERT INTO [Doctors] ([Id],[ClinicId],[Specialty],[Email],[FirstName],[LastName],[Phone],[CreatedDate],[IsDeleted]) " +
            "VALUES (@d,@c,N'General',N'doc@test.local',N'Dana',N'Doc',N'',SYSUTCDATETIME(),0)",
            ("@d", doctorId), ("@c", clinicId));
    }

    private Task InsertLegacyAppointmentAsync(Guid id, Guid clinicId, Guid doctorId, string patientName, DateTime start, DateTime end) =>
        ExecuteAsync(
            "INSERT INTO [Appointments] ([Id],[ClinicId],[DoctorId],[PatientName],[ScheduledStartUtc],[ScheduledEndUtc],[Status],[CreatedDate],[IsDeleted]) " +
            "VALUES (@id,@c,@d,@n,@s,@e,N'Scheduled',SYSUTCDATETIME(),0)",
            ("@id", id), ("@c", clinicId), ("@d", doctorId), ("@n", patientName), ("@s", start), ("@e", end));

    private async Task<SqlConnection> OpenAsync()
    {
        var connection = new SqlConnection(ConnectionString(_databaseName));
        await connection.OpenAsync();
        return connection;
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<object?[]>> QueryAsync(SqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var rows = new List<object?[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < row.Length; i++)
            {
                row[i] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }

    private static async Task<int> ScalarAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<bool> ColumnExistsAsync(SqlConnection connection, string table, string column) =>
        (await QueryAsync(connection,
            "SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(@t) AND name = @c",
            ("@t", $"dbo.{table}"), ("@c", column))).Count == 1;

    private static async Task<bool> IsNullableAsync(SqlConnection connection, string table, string column)
    {
        var rows = await QueryAsync(connection,
            "SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID(@t) AND name = @c",
            ("@t", $"dbo.{table}"), ("@c", column));
        return (bool)Assert.Single(rows)[0]!;
    }

    private static async Task<bool> HistoryHasAsync(SqlConnection connection, string migrationId) =>
        (await QueryAsync(connection,
            "SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = @m", ("@m", migrationId))).Count == 1;

    private static async Task<Dictionary<string, long>> RowCountsAsync(SqlConnection connection)
    {
        var counts = new Dictionary<string, long>();
        foreach (var table in Tables)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT_BIG(*) FROM [dbo].[{table}]";
            counts[table] = (long)(await command.ExecuteScalarAsync())!;
        }

        return counts;
    }

    /// <summary>Columns, defaults, indexes and foreign keys of the tables the migration touches, as one string.</summary>
    private static async Task<string> SchemaFingerprintAsync(SqlConnection connection)
    {
        var tableList = string.Join(",", Tables.Select(t => $"N'{t}'"));
        var lines = new List<string>();

        var columns = await QueryAsync(connection,
            $"""
            SELECT t.name, c.name, ty.name, c.max_length, c.precision, c.scale, c.is_nullable, ISNULL(dc.name, N''), ISNULL(dc.definition, N'')
            FROM sys.tables t
            JOIN sys.columns c ON c.object_id = t.object_id
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            LEFT JOIN sys.default_constraints dc ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
            WHERE t.name IN ({tableList})
            """);
        lines.AddRange(columns.Select(r => "COL " + string.Join("|", r)));

        var indexes = await QueryAsync(connection,
            $"""
            SELECT t.name, i.name, i.is_unique, i.is_primary_key, i.type_desc,
                   STRING_AGG(CONCAT(c.name, CASE WHEN ic.is_descending_key = 1 THEN N' DESC' ELSE N'' END, CASE WHEN ic.is_included_column = 1 THEN N' INC' ELSE N'' END), N',')
                       WITHIN GROUP (ORDER BY ic.is_included_column, ic.key_ordinal, ic.index_column_id)
            FROM sys.tables t
            JOIN sys.indexes i ON i.object_id = t.object_id AND i.type > 0
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE t.name IN ({tableList})
            GROUP BY t.name, i.name, i.is_unique, i.is_primary_key, i.type_desc
            """);
        lines.AddRange(indexes.Select(r => "IDX " + string.Join("|", r)));

        var foreignKeys = await QueryAsync(connection,
            $"""
            SELECT fk.name, pt.name, rt.name, fk.delete_referential_action_desc,
                   STRING_AGG(CONCAT(pc.name, N'->', rc.name), N',') WITHIN GROUP (ORDER BY fkc.constraint_column_id)
            FROM sys.foreign_keys fk
            JOIN sys.tables pt ON pt.object_id = fk.parent_object_id
            JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
            JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
            WHERE pt.name IN ({tableList}) OR rt.name IN ({tableList})
            GROUP BY fk.name, pt.name, rt.name, fk.delete_referential_action_desc
            """);
        lines.AddRange(foreignKeys.Select(r => "FK " + string.Join("|", r)));

        lines.Sort(StringComparer.Ordinal);
        return string.Join("\n", lines);
    }
}
