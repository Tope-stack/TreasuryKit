using TreasuryKit.Core.Domain.Statements;

namespace TreasuryKit.Core.Tests.Statements;

public sealed class LineFingerprintTests
{
    private static readonly Guid Account = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
    private static readonly DateOnly Day = new(2026, 1, 15);

    [Fact]
    public void Is_stable_for_a_known_input()
    {
        // Pinned: changing the fingerprint format breaks de-duplication of stored lines (ADR 0005).
        var fingerprint = LineFingerprint.Compute(Account, Day, -4.00m, null, "SMS ALERT CHARGES", 0);

        Assert.Equal(64, fingerprint.Length);
        Assert.Equal(fingerprint, LineFingerprint.Compute(Account, Day, -4m, null, "  sms   alert charges ", 0));
        Assert.Equal("3d5e52fd", fingerprint[..8]); // independently checked: sha256("0f8fad5b…950e|2026-01-15|-4.0000|N:SMS ALERT CHARGES|0")
    }

    [Fact]
    public void Identical_lines_in_one_file_get_distinct_fingerprints()
    {
        var lines = new[] { (Day, -4.00m, (string?)null, "SMS ALERT CHARGES"), (Day, -4.00m, null, "SMS ALERT CHARGES") };

        var fingerprints = LineFingerprint.ComputeAll(Account, lines, l => l);

        Assert.NotEqual(fingerprints[0], fingerprints[1]);
    }

    [Fact]
    public void Overlapping_file_reproduces_the_same_fingerprints()
    {
        (DateOnly, decimal, string?, string)[] first =
        [
            (Day.AddDays(-1), 10m, "R0", "EARLIER"),
            (Day, -4.00m, null, "SMS ALERT CHARGES"),
            (Day, -4.00m, null, "SMS ALERT CHARGES"),
        ];
        var second = first.Skip(1).Append((Day.AddDays(1), 99m, "R9", "LATER")).ToArray();

        var a = LineFingerprint.ComputeAll(Account, first, l => l);
        var b = LineFingerprint.ComputeAll(Account, second, l => l);

        Assert.Equal(a.Skip(1), b.Take(2));
    }

    [Fact]
    public void Reference_takes_precedence_over_narration()
    {
        var withRef1 = LineFingerprint.Compute(Account, Day, 100m, "FT123", "NARRATION ONE", 0);
        var withRef2 = LineFingerprint.Compute(Account, Day, 100m, "FT123", "NARRATION TWO", 0);
        var otherRef = LineFingerprint.Compute(Account, Day, 100m, "FT124", "NARRATION ONE", 0);

        Assert.Equal(withRef1, withRef2);
        Assert.NotEqual(withRef1, otherRef);
    }

    [Fact]
    public void Account_date_and_amount_are_part_of_identity()
    {
        var baseline = LineFingerprint.Compute(Account, Day, 100m, "FT1", "X", 0);

        Assert.NotEqual(baseline, LineFingerprint.Compute(Guid.NewGuid(), Day, 100m, "FT1", "X", 0));
        Assert.NotEqual(baseline, LineFingerprint.Compute(Account, Day.AddDays(1), 100m, "FT1", "X", 0));
        Assert.NotEqual(baseline, LineFingerprint.Compute(Account, Day, -100m, "FT1", "X", 0));
    }
}
