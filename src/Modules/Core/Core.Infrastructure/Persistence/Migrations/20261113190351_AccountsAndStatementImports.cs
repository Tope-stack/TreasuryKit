using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TreasuryKit.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountsAndStatementImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bank_accounts",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    bank_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    account_number = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    mapping_profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "counterparties",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    aliases = table.Column<List<string>>(type: "text[]", nullable: false),
                    country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    default_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_counterparties", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "jobs",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    run_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mapping_profiles",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    bank_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    file_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    header_row_index = table.Column<int>(type: "integer", nullable: false),
                    skip_rows = table.Column<int>(type: "integer", nullable: false),
                    delimiter = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    date_formats = table.Column<List<string>>(type: "text[]", nullable: false),
                    culture = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    column_map = table.Column<string>(type: "jsonb", nullable: false),
                    amount_mode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    opening_balance_locator = table.Column<string>(type: "jsonb", nullable: true),
                    closing_balance_locator = table.Column<string>(type: "jsonb", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mapping_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "statement_imports",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    file_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    file_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    format = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: true),
                    period_end = table.Column<DateOnly>(type: "date", nullable: true),
                    opening_balance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    closing_balance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    line_count = table.Column<int>(type: "integer", nullable: false),
                    duplicate_line_count = table.Column<int>(type: "integer", nullable: false),
                    balance_validated = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    imported_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    acknowledged_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_statement_imports", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "statement_lines",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    import_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_number = table.Column<int>(type: "integer", nullable: false),
                    booking_date = table.Column<DateOnly>(type: "date", nullable: false),
                    value_date = table.Column<DateOnly>(type: "date", nullable: false),
                    narration = table.Column<string>(type: "text", nullable: false),
                    normalized_narration = table.Column<string>(type: "text", nullable: false),
                    bank_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    extracted_refs = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    counterparty_name_guess = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    fingerprint = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    running_balance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_statement_lines", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bank_accounts_tenant_id_bank_code_account_number_currency",
                schema: "core",
                table: "bank_accounts",
                columns: new[] { "tenant_id", "bank_code", "account_number", "currency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_counterparties_tenant_id_normalized_name",
                schema: "core",
                table: "counterparties",
                columns: new[] { "tenant_id", "normalized_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_jobs_run_after",
                schema: "core",
                table: "jobs",
                column: "run_after",
                filter: "completed_at IS NULL AND failed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_mapping_profiles_tenant_id",
                schema: "core",
                table: "mapping_profiles",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_statement_imports_account_id_imported_at",
                schema: "core",
                table: "statement_imports",
                columns: new[] { "account_id", "imported_at" });

            migrationBuilder.CreateIndex(
                name: "ix_statement_imports_tenant_id_account_id_file_hash",
                schema: "core",
                table: "statement_imports",
                columns: new[] { "tenant_id", "account_id", "file_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_statement_lines_account_id_value_date",
                schema: "core",
                table: "statement_lines",
                columns: new[] { "account_id", "value_date" });

            migrationBuilder.CreateIndex(
                name: "ix_statement_lines_extracted_refs",
                schema: "core",
                table: "statement_lines",
                column: "extracted_refs")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_statement_lines_import_id",
                schema: "core",
                table: "statement_lines",
                column: "import_id");

            migrationBuilder.CreateIndex(
                name: "ix_statement_lines_tenant_id_account_id_fingerprint",
                schema: "core",
                table: "statement_lines",
                columns: new[] { "tenant_id", "account_id", "fingerprint" },
                unique: true);

            // Not expressible in the model: EF cannot index complex-type columns.
            migrationBuilder.Sql("CREATE INDEX ix_statement_lines_account_id_amount ON core.statement_lines (account_id, amount);");

            // System-wide mapping profiles for the generic layouts produced by tools/StatementGen (tenant_id NULL).
            migrationBuilder.Sql("""
                INSERT INTO core.mapping_profiles
                    (id, tenant_id, bank_code, name, file_type, header_row_index, skip_rows, delimiter, date_formats, culture,
                     column_map, amount_mode, opening_balance_locator, closing_balance_locator)
                VALUES
                ('5e3d6a10-0000-4000-8000-00000000000a', NULL, 'GENERIC', 'Generic layout A: CSV, signed amount, dd/MM/yyyy', 'Csv', 0, 0, ',', ARRAY['dd/MM/yyyy']::text[], 'en-NG', '{"bookingDate":["Trans Date","Transaction Date"],"valueDate":["Value Date"],"narration":["Description","Narration"],"reference":["Reference"],"amount":["Amount"],"debit":null,"credit":null,"balance":["Balance"],"currency":null}'::jsonb, 'Signed', NULL, NULL),
                ('5e3d6a10-0000-4000-8000-00000000000b', NULL, 'GENERIC', 'Generic layout B: XLSX, debit/credit, balance rows', 'Xlsx', 6, 0, NULL, ARRAY['dd/MM/yyyy','dd-MMM-yyyy']::text[], 'en-NG', '{"bookingDate":["Post Date"],"valueDate":["Value Date"],"narration":["Narration"],"reference":["Ref","Reference"],"amount":null,"debit":["Debit"],"credit":["Credit"],"balance":["Balance"],"currency":null}'::jsonb, 'DebitCreditColumns', '{"label":"Opening Balance"}'::jsonb, '{"label":"Closing Balance"}'::jsonb),
                ('5e3d6a10-0000-4000-8000-00000000000c', NULL, 'GENERIC', 'Generic layout C: CSV, withdrawals/deposits, dd-MMM-yyyy', 'Csv', 0, 0, ',', ARRAY['dd-MMM-yyyy']::text[], 'en-NG', '{"bookingDate":["Txn Date"],"valueDate":null,"narration":["Remarks"],"reference":["Ref No"],"amount":null,"debit":["Withdrawals"],"credit":["Deposits"],"balance":["Running Balance"],"currency":null}'::jsonb, 'DebitCreditColumns', NULL, NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS core.ix_statement_lines_account_id_amount;");

            migrationBuilder.DropTable(
                name: "bank_accounts",
                schema: "core");

            migrationBuilder.DropTable(
                name: "counterparties",
                schema: "core");

            migrationBuilder.DropTable(
                name: "jobs",
                schema: "core");

            migrationBuilder.DropTable(
                name: "mapping_profiles",
                schema: "core");

            migrationBuilder.DropTable(
                name: "statement_imports",
                schema: "core");

            migrationBuilder.DropTable(
                name: "statement_lines",
                schema: "core");
        }
    }
}
