using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlignDoctorSchemaWithDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Doctors_ClinicId_FullName",
                table: "Doctors");

            migrationBuilder.RenameColumn(
                name: "Specialization",
                table: "Doctors",
                newName: "Specialty");

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Doctors",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "Doctors",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "Doctors",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Doctors",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                UPDATE [Doctors]
                SET
                    [FirstName] = CASE
                        WHEN LTRIM(RTRIM([FullName])) = '' THEN ''
                        WHEN CHARINDEX(' ', LTRIM(RTRIM([FullName]))) > 0 THEN LEFT(LTRIM(RTRIM([FullName])), CHARINDEX(' ', LTRIM(RTRIM([FullName]))) - 1)
                        ELSE LTRIM(RTRIM([FullName]))
                    END,
                    [LastName] = CASE
                        WHEN CHARINDEX(' ', LTRIM(RTRIM([FullName]))) > 0 THEN LTRIM(SUBSTRING(LTRIM(RTRIM([FullName])), CHARINDEX(' ', LTRIM(RTRIM([FullName]))) + 1, LEN(LTRIM(RTRIM([FullName])))))
                        ELSE ''
                    END
                """);

            migrationBuilder.DropColumn(
                name: "FullName",
                table: "Doctors");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Doctors");

            migrationBuilder.DropColumn(
                name: "WorkDayEnd",
                table: "Doctors");

            migrationBuilder.DropColumn(
                name: "WorkDayStart",
                table: "Doctors");

            migrationBuilder.CreateIndex(
                name: "IX_Doctors_ClinicId",
                table: "Doctors",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_Doctors_ClinicId_LastName_FirstName",
                table: "Doctors",
                columns: new[] { "ClinicId", "LastName", "FirstName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Doctors_ClinicId",
                table: "Doctors");

            migrationBuilder.DropIndex(
                name: "IX_Doctors_ClinicId_LastName_FirstName",
                table: "Doctors");

            migrationBuilder.AddColumn<string>(
                name: "FullName",
                table: "Doctors",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Doctors",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "WorkDayEnd",
                table: "Doctors",
                type: "time",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));

            migrationBuilder.AddColumn<TimeSpan>(
                name: "WorkDayStart",
                table: "Doctors",
                type: "time",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));

            migrationBuilder.Sql(
                """
                UPDATE [Doctors]
                SET [FullName] = LTRIM(RTRIM(CONCAT([FirstName], ' ', [LastName])))
                """);

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Doctors");

            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "Doctors");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "Doctors");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "Doctors");

            migrationBuilder.RenameColumn(
                name: "Specialty",
                table: "Doctors",
                newName: "Specialization");

            migrationBuilder.CreateIndex(
                name: "IX_Doctors_ClinicId_FullName",
                table: "Doctors",
                columns: new[] { "ClinicId", "FullName" });
        }
    }
}
