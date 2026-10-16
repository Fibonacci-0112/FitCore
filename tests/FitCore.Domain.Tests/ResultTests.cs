using FitCore.Domain.Quantities;

namespace FitCore.Domain.Tests;

public sealed class ResultTests
{
    [Fact]
    public void A_success_has_a_value_and_no_error()
    {
        Result<int, QuantityError> result = 5;

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value);
        Assert.Throws<InvalidOperationException>(() => result.Error);
        Assert.Equal("Success(5)", result.ToString());
    }

    [Fact]
    public void A_failure_has_an_error_and_no_value()
    {
        Result<int, QuantityError> result = QuantityError.Unconvertible;

        Assert.False(result.IsSuccess);
        Assert.Equal(QuantityError.Unconvertible, result.Error);
        Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Equal("Failure(Unconvertible)", result.ToString());
    }
}
