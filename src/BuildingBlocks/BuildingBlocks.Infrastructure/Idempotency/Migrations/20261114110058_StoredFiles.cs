using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TreasuryKit.BuildingBlocks.Infrastructure.Idempotency.Migrations
{
    /// <inheritdoc />
    public partial class StoredFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stored_files",
                schema: "platform",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stored_files", x => x.key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stored_files",
                schema: "platform");
        }
    }
}
