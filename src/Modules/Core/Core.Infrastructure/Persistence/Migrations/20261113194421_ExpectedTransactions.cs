using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TreasuryKit.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpectedTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "expected_transaction_imports",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    file_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    file_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created = table.Column<int>(type: "integer", nullable: false),
                    updated = table.Column<int>(type: "integer", nullable: false),
                    unchanged = table.Column<int>(type: "integer", nullable: false),
                    skipped = table.Column<int>(type: "integer", nullable: false),
                    errors = table.Column<string>(type: "jsonb", nullable: false),
                    imported_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expected_transaction_imports", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "expected_transactions",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    outstanding_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    expected_date = table.Column<DateOnly>(type: "date", nullable: false),
                    counterparty_id = table.Column<Guid>(type: "uuid", nullable: true),
                    counterparty_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    canonical_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expected_transactions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_expected_transaction_imports_tenant_id_file_hash",
                schema: "core",
                table: "expected_transaction_imports",
                columns: new[] { "tenant_id", "file_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expected_transactions_canonical_reference",
                schema: "core",
                table: "expected_transactions",
                column: "canonical_reference");

            migrationBuilder.CreateIndex(
                name: "ix_expected_transactions_tenant_id_source_external_id",
                schema: "core",
                table: "expected_transactions",
                columns: new[] { "tenant_id", "source", "external_id" },
                unique: true,
                filter: "external_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_expected_transactions_tenant_id_status_expected_date",
                schema: "core",
                table: "expected_transactions",
                columns: new[] { "tenant_id", "status", "expected_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "expected_transaction_imports",
                schema: "core");

            migrationBuilder.DropTable(
                name: "expected_transactions",
                schema: "core");
        }
    }
}
