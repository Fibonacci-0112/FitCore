namespace FitCore.Contracts;

/// <summary>Route prefixes of the public API (ADR 0003 decision 4).</summary>
public static class ApiRoutes
{
    /// <summary>Version 1. Released apps keep working against it for at least 90 days after a newer version ships (NFR-CMP-5).</summary>
    public const string V1 = "/v1";
}
