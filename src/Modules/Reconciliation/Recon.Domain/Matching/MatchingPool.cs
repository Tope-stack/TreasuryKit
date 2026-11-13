namespace TreasuryKit.Recon.Domain.Matching;

public enum ExceptionReason
{
    NoCandidate,
    MultipleCandidates,
    BelowThreshold,
    CurrencyMismatch,
}

/// <summary>
/// Unmatched statement lines and open expected transactions for one account and period. Rules read it and may flag
/// items; the engine removes items as candidates are accepted so later rules see a shrinking pool (plan §5.2).
/// </summary>
public sealed class MatchingPool
{
    private readonly Dictionary<Guid, PoolLine> _lines;
    private readonly Dictionary<Guid, PoolExpected> _expected;
    private readonly Dictionary<(ItemType, Guid), ExceptionReason> _flags = [];

    public MatchingPool(IEnumerable<PoolLine> lines, IEnumerable<PoolExpected> expected)
    {
        _lines = lines.ToDictionary(l => l.Id);
        _expected = expected.ToDictionary(e => e.Id);
    }

    public IReadOnlyCollection<PoolLine> Lines => _lines.Values;

    public IReadOnlyCollection<PoolExpected> Expected => _expected.Values;

    public bool Contains(ItemType type, Guid id) =>
        type == ItemType.StatementLine ? _lines.ContainsKey(id) : _expected.ContainsKey(id);

    public void Remove(ItemType type, Guid id)
    {
        if (type == ItemType.StatementLine)
        {
            _lines.Remove(id);
        }
        else
        {
            _expected.Remove(id);
        }
    }

    /// <summary>Records why an item could not be matched; the most specific reason wins.</summary>
    public void Flag(ItemType type, Guid id, ExceptionReason reason)
    {
        if (!_flags.TryGetValue((type, id), out var existing) || Priority(reason) > Priority(existing))
        {
            _flags[(type, id)] = reason;
        }
    }

    public ExceptionReason ReasonFor(ItemType type, Guid id) =>
        _flags.TryGetValue((type, id), out var reason) ? reason : ExceptionReason.NoCandidate;

    private static int Priority(ExceptionReason reason) => reason switch
    {
        ExceptionReason.CurrencyMismatch => 3,
        ExceptionReason.MultipleCandidates => 2,
        ExceptionReason.BelowThreshold => 1,
        _ => 0,
    };
}
