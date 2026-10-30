namespace CseForms.Application.Common;

/// <summary>
/// Date du jour dans le fuseau du CSE (France métropolitaine). Les horodatages restent stockés en UTC ;
/// ce fuseau ne sert qu'aux règles « aujourd'hui » et à l'année du numéro de dossier.
/// </summary>
public sealed class CseCalendar(TimeProvider timeProvider)
{
    private static readonly TimeZoneInfo TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, TimeZone).DateTime);

    /// <summary>Heure locale du CSE, pour l'affichage.</summary>
    public static DateTimeOffset ToLocal(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, TimeZone);

    /// <summary>Instant UTC du début d'une journée à Paris (bornes des filtres par période).</summary>
    public static DateTimeOffset StartOfDayUtc(DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, TimeZone), TimeSpan.Zero);
    }
}
