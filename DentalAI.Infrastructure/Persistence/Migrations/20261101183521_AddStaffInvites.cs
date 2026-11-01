using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "InviteExpiresAtUtc",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InviteTokenHash",
                table: "Users",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_InviteTokenHash",
                table: "Users",
                column: "InviteTokenHash",
                unique: true,
                filter: "[InviteTokenHash] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_InviteTokenHash",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "InviteExpiresAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "InviteTokenHash",
                table: "Users");
        }
    }
}
