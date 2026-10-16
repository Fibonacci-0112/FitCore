namespace FitCore.Contracts.Profiles;

/// <summary>
/// <c>GET /v1/profile</c>: the signed-in member's profile settings. The birth date stays on the server; clients need
/// only the settings that shape what they show.
/// </summary>
/// <param name="DisplayName">The name FitCore greets the member by, if they gave one.</param>
/// <param name="TimeZone">The IANA time zone that diary dates follow (BRD §6.1).</param>
/// <param name="UnitSystem"><c>us_customary</c> or <c>metric</c>.</param>
/// <param name="Currency">The ISO 4217 code prices are shown in (BRD §6.1).</param>
public sealed record ProfileResponse(string? DisplayName, string TimeZone, string UnitSystem, string Currency);
