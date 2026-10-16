using System.Text;
using System.Text.Json;

namespace FitCore.Platform.Data;

/// <summary>
/// The caller a unit of work runs as: the <c>sub</c> and <c>aal</c> of their validated Supabase access token. RLS
/// policies read these through <c>auth.uid()</c> and <c>auth.jwt()</c>.
/// </summary>
/// <remarks>The claims hold no health data (NFR-PRV-6).</remarks>
public sealed record DbIdentity
{
    public DbIdentity(Guid subject, string assuranceLevel = "aal1")
    {
        if (subject == Guid.Empty)
        {
            throw new ArgumentException("The subject must be a user id.", nameof(subject));
        }

        if (assuranceLevel is not ("aal1" or "aal2"))
        {
            throw new ArgumentException("The assurance level must be aal1 or aal2.", nameof(assuranceLevel));
        }

        Subject = subject;
        AssuranceLevel = assuranceLevel;
    }

    /// <summary>The user id, <c>auth.uid()</c>.</summary>
    public Guid Subject { get; }

    /// <summary><c>aal1</c>, or <c>aal2</c> after a second factor. Clinician and admin access needs <c>aal2</c> (NFR-SEC-3).</summary>
    public string AssuranceLevel { get; }

    /// <summary>The claims as <c>request.jwt.claims</c> expects them, in the shape of a Supabase access token.</summary>
    public string ToClaimsJson()
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteString("sub", Subject);
            json.WriteString("role", "authenticated");
            json.WriteString("aud", "authenticated");
            json.WriteString("aal", AssuranceLevel);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
