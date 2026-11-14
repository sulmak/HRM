namespace HRM.Web.Services;

public static class DateRules
{
    /// <summary>Parses "Friday,Saturday" into a set of days. Empty means no weekly off.</summary>
    public static HashSet<DayOfWeek> ParseOffDays(string? csv)
    {
        var set = new HashSet<DayOfWeek>();
        if (string.IsNullOrWhiteSpace(csv)) return set;
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<DayOfWeek>(part, true, out var d)) set.Add(d);
        }
        return set;
    }

    public static string JoinOffDays(IEnumerable<string> days)
    {
        var parsed = days
            .Select(d => Enum.TryParse<DayOfWeek>(d, true, out var x) ? (DayOfWeek?)x : null)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .OrderBy(x => (int)x);
        return string.Join(",", parsed);
    }

    public static IEnumerable<DateOnly> Range(DateOnly from, DateOnly to)
    {
        for (var d = from; d <= to; d = d.AddDays(1)) yield return d;
    }

    public static DateOnly Max(DateOnly a, DateOnly b) => a >= b ? a : b;
    public static DateOnly Min(DateOnly a, DateOnly b) => a <= b ? a : b;
}
