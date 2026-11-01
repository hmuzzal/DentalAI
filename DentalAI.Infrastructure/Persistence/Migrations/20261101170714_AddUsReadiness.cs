using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUsReadiness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AddressLine1",
                table: "Patients",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressLine2",
                table: "Patients",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Patients",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "Patients",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreferredContactMethod",
                table: "Patients",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Phone");

            migrationBuilder.AddColumn<bool>(
                name: "SmsOptIn",
                table: "Patients",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "SmsOptInAtUtc",
                table: "Patients",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SmsOptOutAtUtc",
                table: "Patients",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "Patients",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "Clinics",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "US");

            // Existing clinics default to US. Dhaka-based ones (the original seed) are in Bangladesh.
            migrationBuilder.Sql("UPDATE [Clinics] SET [Country] = 'BD' WHERE [TimeZone] = 'Asia/Dhaka';");

            migrationBuilder.CreateTable(
                name: "PatientInsurances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CarrierName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    MemberId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    GroupNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RelationshipToPatient = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SubscriberName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    SubscriberDateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientInsurances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatientInsurances_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PatientInsurances_ClinicId",
                table: "PatientInsurances",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientInsurances_PatientId_Priority",
                table: "PatientInsurances",
                columns: new[] { "PatientId", "Priority" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PatientInsurances");

            migrationBuilder.DropColumn(
                name: "AddressLine1",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "AddressLine2",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "PreferredContactMethod",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "SmsOptIn",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "SmsOptInAtUtc",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "SmsOptOutAtUtc",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "State",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "Clinics");
        }
    }
}
