using System.Text.Json;
using TreasuryKit.Core.Domain.Statements.Narration;

namespace TreasuryKit.Core.Tests.Statements;

public sealed record NarrationCase(string Narration, string? NipSessionId, List<string> InvoiceRefs, string? Counterparty)
{
    public override string ToString() => Narration;
}

/// <summary>Phase 2 acceptance: NIP session ids extracted with 100% accuracy on the test set.</summary>
public sealed class NarrationAnalysisTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static TheoryData<NarrationCase> HandWritten => new(Load("narrations.json"));

    public static TheoryData<NarrationCase> Generated => new(Load(Path.Combine("statements", "narration-answer-key.json")));

    [Theory]
    [MemberData(nameof(HandWritten))]
    public void Hand_written_cases(NarrationCase c) => AssertExtraction(c);

    [Theory]
    [MemberData(nameof(Generated))]
    public void Generated_statement_narrations(NarrationCase c) => AssertExtraction(c);

    [Fact]
    public void Nip_accuracy_is_100_percent_across_the_test_set()
    {
        var cases = Load("narrations.json").Concat(Load(Path.Combine("statements", "narration-answer-key.json"))).ToList();

        var correct = cases.Count(c => Extract(c.Narration).References.NipSessionId == c.NipSessionId);

        Assert.True(cases.Count(c => c.NipSessionId is not null) >= 60, "test set should contain at least 60 session ids");
        Assert.Equal(cases.Count, correct);
    }

    [Theory]
    [InlineData("NIP/FRM ADEBAYO TRADING LTD/000013260115143022123456789012/INV-2001", "ADEBAYO TRADING LTD 000013260115143022123456789012 INV-2001")]
    [InlineData("nip trf to kano agro supplies", "KANO AGRO SUPPLIES")]
    [InlineData("USSD/TRF/FRM  Eko Foods, Plc.", "EKO FOODS PLC")]
    [InlineData("POS/WEB PURCHASE SHOPRITE", "PURCHASE SHOPRITE")]
    [InlineData("OKONKWO & SONS - LAGOS", "OKONKWO & SONS LAGOS")]
    [InlineData("TO", "TO")]
    [InlineData("TRANSFER TO JOS", "TRANSFER TO JOS")]
    public void Normalizer_strips_channel_prefixes_and_noise(string input, string expected) =>
        Assert.Equal(expected, new NarrationNormalizer().Normalize(input));

    [Fact]
    public void Normalizer_prefixes_are_configurable() =>
        Assert.Equal("NIP ACME", new NarrationNormalizer(["BANKX"]).Normalize("BANKX/NIP ACME"));

    [Fact]
    public void Tenant_patterns_replace_the_default()
    {
        var patterns = InvoiceReferencePatterns.Create([@"BILL-\d{4}", @"\bSO\d+"]).Value;

        var refs = ReferenceExtractor.Default.Extract("PAYMENT BILL-2026 AND SO991 INV-1", ExtractionHints.None, patterns).References;

        Assert.Equal(["BILL2026", "SO991"], refs.InvoiceRefs);
    }

    [Theory]
    [InlineData("(unclosed")]
    [InlineData("[z-a]")]
    public void Invalid_patterns_are_rejected(string pattern) =>
        Assert.Equal("ReferencePatterns.Invalid", InvoiceReferencePatterns.Create([pattern]).Error.Code);

    [Fact]
    public void Too_many_patterns_are_rejected() =>
        Assert.True(InvoiceReferencePatterns.Create(Enumerable.Range(0, 11).Select(i => $"P{i}")).IsFailure);

    [Fact]
    public void Catastrophic_pattern_times_out_instead_of_hanging()
    {
        var patterns = InvoiceReferencePatterns.Create([@"(A+)+$"]).Value;
        var started = DateTime.UtcNow;

        var refs = ReferenceExtractor.Default.Extract(new string('A', 5000) + "!", ExtractionHints.None, patterns).References;

        Assert.Empty(refs.InvoiceRefs);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Structured_hints_win_over_guesses()
    {
        var result = ReferenceExtractor.Default.Extract(
            "TRF FRM SOMEONE ELSE",
            new ExtractionHints("E2E-ABC-123", "Okonkwo & Sons Ltd."),
            InvoiceReferencePatterns.Default);

        Assert.Equal("OKONKWO SONS LTD", result.CounterpartyNameGuess);
        Assert.Contains("E2E-ABC-123", result.References.PaymentRefs);
    }

    [Fact]
    public void Payment_references_include_uetrs_and_16_character_tokens()
    {
        var refs = ReferenceExtractor.Default.Extract(
            "SWIFT IN UETR 3f2504e0-4f89-41d3-9a0c-0305e82c3301 REF AB12CD34EF56GH78 ACCT 1234567890123456",
            ExtractionHints.None,
            InvoiceReferencePatterns.Default).References;

        Assert.Contains("3F2504E0-4F89-41D3-9A0C-0305E82C3301", refs.PaymentRefs);
        Assert.Contains("AB12CD34EF56GH78", refs.PaymentRefs);
        Assert.DoesNotContain("1234567890123456", refs.PaymentRefs); // digits only: not a payment reference
    }

    private static void AssertExtraction(NarrationCase c)
    {
        var result = Extract(c.Narration);

        Assert.Equal(c.NipSessionId, result.References.NipSessionId);
        Assert.Equal(c.InvoiceRefs, result.References.InvoiceRefs);
        Assert.Equal(c.Counterparty, result.CounterpartyNameGuess);
    }

    private static ExtractionResult Extract(string narration) =>
        ReferenceExtractor.Default.Extract(narration, ExtractionHints.None, InvoiceReferencePatterns.Default);

    private static List<NarrationCase> Load(string relativePath) =>
        JsonSerializer.Deserialize<List<NarrationCase>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", relativePath)), Json)!;
}
