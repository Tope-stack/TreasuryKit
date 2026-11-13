using TreasuryKit.BuildingBlocks.Domain;

namespace TreasuryKit.BuildingBlocks.Tests;

public sealed class ResultTests
{
    private static readonly Error SomeError = Error.Validation("Test.Invalid", "Invalid.");

    [Fact]
    public void Success_carries_value()
    {
        Result<int> result = 42;

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void Failure_carries_error_and_hides_value()
    {
        Result<int> result = SomeError;

        Assert.True(result.IsFailure);
        Assert.Equal(SomeError, result.Error);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Non_generic_failure_requires_an_error() =>
        Assert.Throws<ArgumentException>(() => Result.Failure(Error.None));
}
