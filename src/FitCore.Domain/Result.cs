namespace FitCore.Domain;

/// <summary>
/// The outcome of a rule that can fail for an expected reason, such as an impossible unit conversion.
/// Rules return a reason code instead of throwing, so callers can show the member why.
/// </summary>
public readonly struct Result<TValue, TError>
    where TError : struct, Enum
{
    private readonly TValue? _value;
    private readonly TError _error;

    private Result(TValue value)
    {
        _value = value;
        _error = default;
        IsSuccess = true;
    }

    private Result(TError error)
    {
        _value = default;
        _error = error;
        IsSuccess = false;
    }

    public bool IsSuccess { get; }

    /// <summary>The value. Throws if the rule failed.</summary>
    public TValue Value => IsSuccess ? _value! : throw new InvalidOperationException($"The result failed with {_error}.");

    /// <summary>The reason the rule failed. Throws if it succeeded.</summary>
    public TError Error => IsSuccess ? throw new InvalidOperationException("The result succeeded.") : _error;

    public static implicit operator Result<TValue, TError>(TValue value) => new(value);

    public static implicit operator Result<TValue, TError>(TError error) => new(error);

    public override string ToString() => IsSuccess ? $"Success({_value})" : $"Failure({_error})";
}
