namespace FitCore.Domain.Costs;

/// <summary>An ISO 4217 currency code, such as <c>USD</c>.</summary>
public readonly record struct CurrencyCode
{
    // ISO 4217 currencies whose minor unit isn't hundredths. Every other code uses 2 digits.
    private static readonly HashSet<string> NoMinorUnit =
        ["BIF", "CLP", "DJF", "GNF", "ISK", "JPY", "KMF", "KRW", "PYG", "RWF", "UGX", "UYI", "VND", "VUV", "XAF", "XOF", "XPF"];

    private static readonly HashSet<string> ThousandthsMinorUnit = ["BHD", "IQD", "JOD", "KWD", "LYD", "OMR", "TND"];

    private static readonly HashSet<string> TenThousandthsMinorUnit = ["CLF", "UYW"];

    private CurrencyCode(string value) => Value = value;

    public static CurrencyCode Usd { get; } = new("USD");

    public string Value { get; }

    /// <summary>How many digits the minor unit has: 2 for USD (cents), 0 for JPY, 3 for KWD, 4 for CLF.</summary>
    public int MinorUnitDigits =>
        NoMinorUnit.Contains(Value) ? 0
        : ThousandthsMinorUnit.Contains(Value) ? 3
        : TenThousandthsMinorUnit.Contains(Value) ? 4
        : 2;

    public static bool TryParse(string? text, out CurrencyCode code)
    {
        if (text is { Length: 3 } && text.All(char.IsAsciiLetterUpper))
        {
            code = new CurrencyCode(text);
            return true;
        }

        code = default;
        return false;
    }

    public static CurrencyCode Parse(string text) =>
        TryParse(text, out var code) ? code : throw new FormatException($"'{text}' is not an ISO 4217 currency code.");

    public override string ToString() => Value;
}

/// <summary>
/// An amount of money as integer minor units plus a currency, such as 349 USD for $3.49.
/// Never a floating-point number (BRD §6.1, NFR-REL-6).
/// </summary>
public readonly record struct Money(long MinorUnits, CurrencyCode Currency)
{
    public static Money Zero(CurrencyCode currency) => new(0, currency);

    /// <summary>Converts an amount in major units, such as 3.49, rounding half away from zero to the currency's minor unit.</summary>
    public static Money FromMajor(decimal major, CurrencyCode currency) =>
        new((long)decimal.Round(major * Pow10(currency.MinorUnitDigits), MidpointRounding.AwayFromZero), currency);

    /// <summary>The amount in major units, such as 3.49.</summary>
    public decimal ToMajor() => MinorUnits / Pow10(Currency.MinorUnitDigits);

    private static decimal Pow10(int digits)
    {
        var factor = 1m;
        for (var i = 0; i < digits; i++)
        {
            factor *= 10;
        }

        return factor;
    }
}
