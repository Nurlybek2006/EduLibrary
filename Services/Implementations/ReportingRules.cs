using System.Globalization;
using System.Text;
using EduLibrary.Helpers;
using EduLibrary.ViewModels;

namespace EduLibrary.Services.Implementations;

public sealed record ReportingPeriod(DateTime Start, DateTime EndExclusive, DateTime UtcStart, DateTime UtcEnd)
{
    public DateTime End => EndExclusive.AddDays(-1);
    public string Label => $"{Start:dd.MM.yyyy} – {End:dd.MM.yyyy}";
}

public static class ReportingRules
{
    public static readonly TimeZoneInfo LibraryTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Qyzylorda");

    public static ReportingPeriod ResolvePeriod(ReportFilterViewModel filter, DateTime today)
    {
        today = today.Date;
        var end = today;
        var start = filter.Range switch
        {
            "7" => today.AddDays(-6), "90" => today.AddDays(-89), "year" => new DateTime(today.Year, 1, 1),
            "custom" => filter.StartDate?.Date ?? today.AddDays(-29), _ => today.AddDays(-29)
        };
        if (filter.Range == "custom") end = filter.EndDate?.Date ?? today;
        else if (filter.Range is not ("7" or "30" or "90" or "year")) filter.Range = "30";
        if (start.Year < 1900 || end > today || end < start || (end - start).Days > 3660)
            throw new BusinessException("Кезеңді 1900 жылдан бүгінге дейін таңдаңыз. Аяқталу күні басталу күнінен ерте болмауы және кезең 10 жылдан аспауы тиіс.");
        filter.StartDate = start;
        filter.EndDate = end;
        var exclusive = end.AddDays(1);
        return new ReportingPeriod(start, exclusive,
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(start, DateTimeKind.Unspecified), LibraryTimeZone),
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(exclusive, DateTimeKind.Unspecified), LibraryTimeZone));
    }

    public static int OverdueDays(DateTime dueDate, DateTime? returnedAt, DateTime today)
        => Math.Max(0, ((returnedAt ?? today).Date - dueDate.Date).Days);

    public static int ReaderActivityScore(int loans, int returns, int overdue) => Math.Max(0, loans * 3 + returns * 2 - overdue * 2);

    public static string CsvField(string? value)
    {
        value ??= "";
        // Excel can interpret a formula after whitespace/control characters too.
        var first = 0;
        while (first < value.Length && (char.IsWhiteSpace(value[first]) || char.IsControl(value[first]))) first++;
        if ((first < value.Length && value[first] is '=' or '+' or '-' or '@')
            || (value.Length > 0 && char.IsControl(value[0]))) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    public static byte[] CreateCsv(IEnumerable<IEnumerable<string?>> rows)
    {
        var content = string.Join("\r\n", rows.Select(row => string.Join(",", row.Select(CsvField)))) + "\r\n";
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(content)).ToArray();
    }

    internal static string Number(int number) => number.ToString("N0", CultureInfo.CurrentCulture);
    internal static string Money(decimal amount) => amount.ToString("N2", CultureInfo.CurrentCulture) + " ₸";
    internal static string Rating(double? rating) => rating.HasValue ? rating.Value.ToString("0.00", CultureInfo.CurrentCulture) + " / 5" : "Баға жоқ";
}
