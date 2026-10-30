using System.Globalization;
using CseForms.Application.Common;

namespace CseForms.Api.BackOffice;

/// <summary>Formats d'affichage : dates au format JJ/MM/AAAA, heure de Paris.</summary>
internal static class Display
{
    public static string DateTime(DateTimeOffset utc) =>
        CseCalendar.ToLocal(utc).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    public static string Date(DateTimeOffset utc) =>
        CseCalendar.ToLocal(utc).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
