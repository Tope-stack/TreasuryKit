using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TreasuryKit.Recon.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MatchingModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "exceptions",
                schema: "recon",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    candidates = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    resolution_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exceptions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "matches",
                schema: "recon",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rule_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    score_breakdown = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    value_date = table.Column<DateOnly>(type: "date", nullable: false),
                    proposed_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    proposed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_matches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "matching_rule_configs",
                schema: "recon",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    parameters = table.Column<string>(type: "jsonb", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_matching_rule_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reconciled_items",
                schema: "recon",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reconciled_items", x => new { x.tenant_id, x.item_type, x.item_id });
                });

            migrationBuilder.CreateTable(
                name: "reconciliation_runs",
                schema: "recon",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    triggered_by = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    requested_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    stats = table.Column<string>(type: "jsonb", nullable: true),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    rerun_requested = table.Column<bool>(type: "boolean", nullable: false),
                    rerun_start = table.Column<DateOnly>(type: "date", nullable: true),
                    rerun_end = table.Column<DateOnly>(type: "date", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reconciliation_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "settings",
                schema: "recon",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    auto_confirm_threshold = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    propose_threshold = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settings", x => x.tenant_id);
                });

            migrationBuilder.CreateTable(
                name: "match_items",
                schema: "recon",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    allocated_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_match_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_match_items_matches_match_id",
                        column: x => x.match_id,
                        principalSchema: "recon",
                        principalTable: "matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_exceptions_account_id_status",
                schema: "recon",
                table: "exceptions",
                columns: new[] { "account_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_exceptions_item_type_item_id",
                schema: "recon",
                table: "exceptions",
                columns: new[] { "item_type", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_match_items_item_type_item_id",
                schema: "recon",
                table: "match_items",
                columns: new[] { "item_type", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_match_items_match_id",
                schema: "recon",
                table: "match_items",
                column: "match_id");

            migrationBuilder.CreateIndex(
                name: "ix_matches_account_id_status_value_date",
                schema: "recon",
                table: "matches",
                columns: new[] { "account_id", "status", "value_date" });

            migrationBuilder.CreateIndex(
                name: "ix_matches_run_id",
                schema: "recon",
                table: "matches",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_matching_rule_configs_tenant_id_rule_key",
                schema: "recon",
                table: "matching_rule_configs",
                columns: new[] { "tenant_id", "rule_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reconciled_items_account_id",
                schema: "recon",
                table: "reconciled_items",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_reconciled_items_match_id",
                schema: "recon",
                table: "reconciled_items",
                column: "match_id");

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_runs_account_id_created_at",
                schema: "recon",
                table: "reconciliation_runs",
                columns: new[] { "account_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_reconciliation_runs_one_active_per_account",
                schema: "recon",
                table: "reconciliation_runs",
                columns: new[] { "tenant_id", "account_id" },
                unique: true,
                filter: "status IN ('Queued', 'Running')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exceptions",
                schema: "recon");

            migrationBuilder.DropTable(
                name: "match_items",
                schema: "recon");

            migrationBuilder.DropTable(
                name: "matching_rule_configs",
                schema: "recon");

            migrationBuilder.DropTable(
                name: "reconciled_items",
                schema: "recon");

            migrationBuilder.DropTable(
                name: "reconciliation_runs",
                schema: "recon");

            migrationBuilder.DropTable(
                name: "settings",
                schema: "recon");

            migrationBuilder.DropTable(
                name: "matches",
                schema: "recon");
        }
    }
}
