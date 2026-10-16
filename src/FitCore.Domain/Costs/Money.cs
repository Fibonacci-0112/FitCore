namespace FitCore.Domain.Costs;

/// <summary>An ISO 4217 currency code with a minor unit, such as <c>USD</c>. Unassigned codes are rejected.</summary>
public readonly record struct CurrencyCode
{
    // ISO 4217 list one (published 2026-09-17): every currency with a numeric minor unit, by its number of digits.
    // Codes without a minor unit (precious metals, bond units, XDR, XSU, XUA, XTS and XXX) are rejected.
    private static readonly Dictionary<string, int> MinorUnits = BuildMinorUnits();

    private CurrencyCode(string value) => Value = value;

    public static CurrencyCode Usd { get; } = new("USD");

    public string Value { get; }

    /// <summary>How many digits the minor unit has: 2 for USD (cents), 0 for JPY, 3 for KWD, 4 for CLF.</summary>
    public int MinorUnitDigits => MinorUnits[Value];

    public static bool TryParse(string? text, out CurrencyCode code)
    {
        if (text is not null && MinorUnits.ContainsKey(text))
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

    private static Dictionary<string, int> BuildMinorUnits()
    {
        string[] none = ["BIF", "CLP", "DJF", "GNF", "ISK", "JPY", "KMF", "KRW", "PYG", "RWF", "UGX", "UYI", "VND", "VUV", "XAF", "XOF", "XPF"];
        string[] hundredths =
        [
            "AED", "AFN", "ALL", "AMD", "AOA", "ARS", "AUD", "AWG", "AZN", "BAM", "BBD", "BDT", "BMD", "BND", "BOB", "BOV", "BRL", "BSD", "BTN", "BWP",
            "BYN", "BZD", "CAD", "CDF", "CHE", "CHF", "CHW", "CNY", "COP", "COU", "CRC", "CUP", "CVE", "CZK", "DKK", "DOP", "DZD", "EGP", "ERN", "ETB",
            "EUR", "FJD", "FKP", "GBP", "GEL", "GHS", "GIP", "GMD", "GTQ", "GYD", "HKD", "HNL", "HTG", "HUF", "IDR", "ILS", "INR", "IRR", "JMD", "KES",
            "KGS", "KHR", "KPW", "KYD", "KZT", "LAK", "LBP", "LKR", "LRD", "LSL", "MAD", "MDL", "MGA", "MKD", "MMK", "MNT", "MOP", "MRU", "MUR", "MVR",
            "MWK", "MXN", "MXV", "MYR", "MZN", "NAD", "NGN", "NIO", "NOK", "NPR", "NZD", "PAB", "PEN", "PGK", "PHP", "PKR", "PLN", "QAR", "RON", "RSD",
            "RUB", "SAR", "SBD", "SCR", "SDG", "SEK", "SGD", "SHP", "SLE", "SOS", "SRD", "SSP", "STN", "SVC", "SYP", "SZL", "THB", "TJS", "TMT", "TOP",
            "TRY", "TTD", "TWD", "TZS", "UAH", "USD", "USN", "UYU", "UZS", "VED", "VES", "WST", "XAD", "XCD", "XCG", "YER", "ZAR", "ZMW", "ZWG"
        ];
        string[] thousandths = ["BHD", "IQD", "JOD", "KWD", "LYD", "OMR", "TND"];
        string[] tenThousandths = ["CLF", "UYW"];

        var units = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (codes, digits) in new[] { (none, 0), (hundredths, 2), (thousandths, 3), (tenThousandths, 4) })
        {
            foreach (var code in codes)
            {
                units.Add(code, digits);
            }
        }

        return units;
    }
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
