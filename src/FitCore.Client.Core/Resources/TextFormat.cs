using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace FitCore.Client.Core.Resources;

/// <summary>Fills in the resource strings that take values, in the member's culture (NFR-L10N-2).</summary>
public static class TextFormat
{
    /// <summary>A browser tab or window title: the page's name and the product's.</summary>
    [SuppressMessage("Performance", "CA1863:Use 'CompositeFormat'", Justification = "The format is a resource that changes with the culture, so a cached one would be wrong.")]
    public static string PageTitle(string page) => string.Format(CultureInfo.CurrentCulture, ShellStrings.PageTitleFormat, page);
}
