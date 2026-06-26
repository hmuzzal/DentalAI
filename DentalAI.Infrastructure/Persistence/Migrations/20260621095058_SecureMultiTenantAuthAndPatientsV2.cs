using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SecureMultiTenantAuthAndPatientsV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Appointments_Services_ServiceId')
                    ALTER TABLE [dbo].[Appointments] DROP CONSTRAINT [FK_Appointments_Services_ServiceId];

                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Appointments_ServiceId' AND object_id = OBJECT_ID(N'dbo.Appointments'))
                    DROP INDEX [IX_Appointments_ServiceId] ON [dbo].[Appointments];

                IF COL_LENGTH('dbo.Appointments', 'ServiceId') IS NOT NULL
                    ALTER TABLE [dbo].[Appointments] DROP COLUMN [ServiceId];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ServiceId",
                table: "Appointments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ServiceId",
                table: "Appointments",
                column: "ServiceId");

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Services_ServiceId",
                table: "Appointments",
                column: "ServiceId",
                principalTable: "Services",
                principalColumn: "Id");
        }
    }
}
