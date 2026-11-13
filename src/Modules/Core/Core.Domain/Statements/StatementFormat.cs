namespace TreasuryKit.Core.Domain.Statements;

public enum StatementFormat
{
    Csv,
    Xlsx,
    Mt940,
    Camt053,
}

public enum StatementImportStatus
{
    Queued,
    Parsing,
    Validated,
    BalanceMismatch,
    Failed,
    Completed,
}
