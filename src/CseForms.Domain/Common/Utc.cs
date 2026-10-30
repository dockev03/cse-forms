namespace CseForms.Domain.Common;

internal static class Utc
{
    /// <summary>Toutes les dates sont stockées en UTC : refuse tout autre décalage.</summary>
    public static DateTimeOffset Require(DateTimeOffset value, string name)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainException($"{name} doit être exprimée en UTC.");
        }

        return value;
    }
}
