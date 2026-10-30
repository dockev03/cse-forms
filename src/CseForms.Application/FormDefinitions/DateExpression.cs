using System.Globalization;
using System.Text.RegularExpressions;

namespace CseForms.Application.FormDefinitions;

/// <summary>Date fixe (<c>AAAA-MM-JJ</c>) ou relative au jour courant (<c>today</c>, <c>today+6M</c>...).</summary>
internal static partial class DateExpression
{
    public const string DateFormat = "yyyy-MM-dd";

    public static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    public static bool IsValid(string? expression) => TryResolve(expression, new DateOnly(5000, 1, 1), out _);

    public static bool TryResolve(string? expression, DateOnly today, out DateOnly date)
    {
        if (TryParseDate(expression, out date))
        {
            return true;
        }

        var match = RelativePattern().Match(expression ?? string.Empty);
        if (!match.Success)
        {
            return false;
        }

        date = today;
        if (!match.Groups["sign"].Success)
        {
            return true;
        }

        var amount = int.Parse(match.Groups["amount"].Value, CultureInfo.InvariantCulture);
        if (match.Groups["sign"].Value == "-")
        {
            amount = -amount;
        }

        date = match.Groups["unit"].Value switch
        {
            "D" => today.AddDays(amount),
            "M" => today.AddMonths(amount),
            _ => today.AddYears(amount),
        };

        return true;
    }

    [GeneratedRegex("^today(?:(?<sign>[+-])(?<amount>[0-9]{1,3})(?<unit>[DMY]))?$")]
    private static partial Regex RelativePattern();
}
