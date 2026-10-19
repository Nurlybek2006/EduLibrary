using System.Globalization;

namespace EduLibrary.Helpers;

/// <summary>Pure pickup calendar rules. Database timestamps are UTC; appointments use the library's zone.</summary>
public static class ReservationSchedule
{
    public static readonly TimeZoneInfo LibraryTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Qyzylorda");

    public static DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), LibraryTimeZone);
    public static DateTime ToUtc(DateTime local) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), LibraryTimeZone);

    public static bool TryGetSlot(DateTime date, string? time, DateTime nowUtc, ReservationOptions options, out DateTime slotUtc)
    {
        slotUtc = default;
        if (!TimeOnly.TryParseExact(time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return false;
        var local = date.Date.Add(parsed.ToTimeSpan());
        if (!IsConfiguredSlot(local, options) || local.Date < ToLocal(nowUtc).Date || local.Date > ToLocal(nowUtc).Date.AddDays(options.MaxAdvanceDays)) return false;
        slotUtc = ToUtc(local);
        return slotUtc >= nowUtc.AddMinutes(options.MinimumLeadMinutes);
    }

    public static bool IsConfiguredSlot(DateTime local, ReservationOptions options)
    {
        if (!options.OpenDays.Contains((int)local.DayOfWeek) || local.Ticks % TimeSpan.TicksPerMinute != 0) return false;
        var opens = TimeOnly.ParseExact(options.OpeningTime, "HH:mm", CultureInfo.InvariantCulture);
        var closes = TimeOnly.ParseExact(options.ClosingTime, "HH:mm", CultureInfo.InvariantCulture);
        var minutes = (local.TimeOfDay - opens.ToTimeSpan()).TotalMinutes;
        return minutes >= 0 && minutes % options.SlotMinutes == 0 && local.TimeOfDay.Add(TimeSpan.FromMinutes(options.SlotMinutes)) <= closes.ToTimeSpan();
    }

    public static IEnumerable<DateTime> EnumerateSlots(DateTime nowUtc, ReservationOptions options)
    {
        var today = ToLocal(nowUtc).Date;
        var opens = TimeOnly.ParseExact(options.OpeningTime, "HH:mm", CultureInfo.InvariantCulture);
        var closes = TimeOnly.ParseExact(options.ClosingTime, "HH:mm", CultureInfo.InvariantCulture);
        for (var day = 0; day <= options.MaxAdvanceDays; day++)
        {
            var date = today.AddDays(day);
            if (!options.OpenDays.Contains((int)date.DayOfWeek)) continue;
            for (var local = date.Add(opens.ToTimeSpan()); local.AddMinutes(options.SlotMinutes) <= date.Add(closes.ToTimeSpan()); local = local.AddMinutes(options.SlotMinutes))
            {
                var utc = ToUtc(local);
                if (utc >= nowUtc.AddMinutes(options.MinimumLeadMinutes)) yield return utc;
            }
        }
    }

    public static DateTime HoldDeadline(DateTime nowUtc, DateTime pickupUtc, int holdHours, ReservationOptions options)
        => new(Math.Max(nowUtc.AddHours(holdHours).Ticks, pickupUtc.AddMinutes(options.PickupGraceMinutes).Ticks), DateTimeKind.Utc);
}
