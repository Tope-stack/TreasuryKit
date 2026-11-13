using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TreasuryKit.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NarrationAnalysisAndStructuredFormats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "invoice_reference_patterns",
                schema: "core",
                table: "tenants",
                type: "text[]",
                nullable: false,
                defaultValueSql: "ARRAY['INV[-/ ]?\\d+']::text[]");

            migrationBuilder.AddColumn<string>(
                name: "counterparty_name",
                schema: "core",
                table: "statement_lines",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "end_to_end_id",
                schema: "core",
                table: "statement_lines",
                type: "character varying(35)",
                maxLength: 35,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "skipped_entry_count",
                schema: "core",
                table: "statement_imports",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "invoice_reference_patterns",
                schema: "core",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "counterparty_name",
                schema: "core",
                table: "statement_lines");

            migrationBuilder.DropColumn(
                name: "end_to_end_id",
                schema: "core",
                table: "statement_lines");

            migrationBuilder.DropColumn(
                name: "skipped_entry_count",
                schema: "core",
                table: "statement_imports");
        }
    }
}
