using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TreasuryKit.Recon.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ManualMatchingAndAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "adjustment",
                schema: "recon",
                table: "matches",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reviewed_at",
                schema: "recon",
                table: "matches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reviewed_by",
                schema: "recon",
                table: "matches",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "adjustment",
                schema: "recon",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                schema: "recon",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "reviewed_by",
                schema: "recon",
                table: "matches");
        }
    }
}
