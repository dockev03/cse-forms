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
}
