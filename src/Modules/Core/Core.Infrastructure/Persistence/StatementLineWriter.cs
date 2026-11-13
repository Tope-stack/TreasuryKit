using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TreasuryKit.Core.Application.Statements.Parsing;
using TreasuryKit.Core.Domain.Statements;

namespace TreasuryKit.Core.Infrastructure.Persistence;

/// <summary>
/// Multi-row INSERT … ON CONFLICT DO NOTHING on (tenant_id, account_id, fingerprint): lines seen in an earlier,
/// overlapping statement are skipped. Executes on the context's connection, inside its current transaction.
/// </summary>
internal sealed class StatementLineWriter(CoreDbContext db) : IStatementLineWriter
{
    private const int BatchSize = 500;

    private static readonly string[] Columns =
    [
        "id", "tenant_id", "account_id", "import_id", "row_number", "booking_date", "value_date", "amount", "currency",
        "narration", "normalized_narration", "bank_reference", "fingerprint", "running_balance",
        "extracted_refs", "counterparty_name_guess", "end_to_end_id", "counterparty_name",
    ];

    public async Task<int> InsertAsync(IReadOnlyList<StatementLine> lines, CancellationToken cancellationToken)
    {
        var inserted = 0;
        foreach (var batch in lines.Chunk(BatchSize))
        {
            var sql = new StringBuilder($"INSERT INTO core.statement_lines ({string.Join(", ", Columns)}) VALUES ");
            var parameters = new List<NpgsqlParameter>(batch.Length * Columns.Length);

            for (var i = 0; i < batch.Length; i++)
            {
                var line = batch[i];
                object?[] values =
                [
                    line.Id, line.TenantId, line.AccountId, line.ImportId, line.RowNumber, line.BookingDate, line.ValueDate,
                    line.Amount.Amount, line.Amount.Currency.Code, line.Narration, line.NormalizedNarration, line.BankReference,
                    line.Fingerprint, line.RunningBalance, line.ExtractedRefs, line.CounterpartyNameGuess, line.EndToEndId,
                    line.CounterpartyName,
                ];

                sql.Append(i == 0 ? "(" : ", (");
                for (var c = 0; c < values.Length; c++)
                {
                    var name = $"p{parameters.Count}";
                    sql.Append(c == 0 ? "@" : ", @").Append(name);
                    if (Columns[c] == "extracted_refs")
                    {
                        sql.Append("::jsonb");
                    }

                    parameters.Add(new NpgsqlParameter(name, values[c] ?? DBNull.Value));
                }

                sql.Append(')');
            }

            sql.Append(" ON CONFLICT (tenant_id, account_id, fingerprint) DO NOTHING");
            inserted += await db.Database.ExecuteSqlRawAsync(sql.ToString(), parameters, cancellationToken);
        }

        return inserted;
    }

    public async Task UpdateAnalysisAsync(IReadOnlyList<LineAnalysis> analyses, CancellationToken cancellationToken)
    {
        foreach (var batch in analyses.Chunk(BatchSize))
        {
            var sql = new StringBuilder(
                "UPDATE core.statement_lines AS l SET normalized_narration = v.normalized, extracted_refs = v.refs, " +
                "counterparty_name_guess = v.guess FROM (VALUES ");
            var parameters = new List<NpgsqlParameter>(batch.Length * 4);
            for (var i = 0; i < batch.Length; i++)
            {
                var a = batch[i];
                var p = parameters.Count;
                sql.Append(i == 0 ? string.Empty : ", ")
                    .Append($"(@p{p}::uuid, @p{p + 1}::text, @p{p + 2}::jsonb, @p{p + 3}::text)");
                parameters.Add(new NpgsqlParameter($"p{p}", a.LineId));
                parameters.Add(new NpgsqlParameter($"p{p + 1}", a.NormalizedNarration));
                parameters.Add(new NpgsqlParameter($"p{p + 2}", a.ExtractedRefs));
                parameters.Add(new NpgsqlParameter($"p{p + 3}", (object?)a.CounterpartyNameGuess ?? DBNull.Value));
            }

            // Tenant guard: raw SQL bypasses the EF query filter, so constrain explicitly.
            sql.Append(") AS v(id, normalized, refs, guess) WHERE l.id = v.id AND l.tenant_id = @tenant");
            parameters.Add(new NpgsqlParameter("tenant", db.CurrentTenantId ?? throw new InvalidOperationException("No current tenant.")));
            await db.Database.ExecuteSqlRawAsync(sql.ToString(), parameters, cancellationToken);
        }
    }
}
