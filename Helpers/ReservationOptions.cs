using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace EduLibrary.Helpers;

public sealed class ReservationOptions
{
    public const string SectionName = "Reservations";
    public string OpeningTime { get; set; } = "09:00";
    public string ClosingTime { get; set; } = "18:00";
    public List<int> OpenDays { get; set; } = [1, 2, 3, 4, 5];
    public int SlotMinutes { get; set; } = 30;
    public int SlotCapacity { get; set; } = 5;
    public int MaxAdvanceDays { get; set; } = 14;
    public int MinimumLeadMinutes { get; set; } = 30;
    public int ReadyHoldHours { get; set; } = 48;
    public int PendingHoldHours { get; set; } = 24;
    public int PickupGraceMinutes { get; set; } = 60;
    public int ExpiringSoonHours { get; set; } = 6;
    public int ReconcileIntervalMinutes { get; set; } = 5;
    public List<PickupLocationOption> PickupLocations { get; set; } = [new()];

    // ConfigurationBinder appends to existing lists. Replace configured lists,
    // while retaining defaults when the corresponding setting is absent.
    public static void Bind(ReservationOptions options, IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        if (section.GetSection(nameof(OpenDays)).Exists()) options.OpenDays = [];
        if (section.GetSection(nameof(PickupLocations)).Exists()) options.PickupLocations = [];
        section.Bind(options);
    }

    public static bool IsValid(ReservationOptions options) =>
        TimeOnly.TryParseExact(options.OpeningTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var opens)
        && TimeOnly.TryParseExact(options.ClosingTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var closes) && opens < closes
        && options.SlotMinutes is >= 10 and <= 180 && (closes - opens).TotalMinutes >= options.SlotMinutes
        && options.SlotCapacity is >= 1 and <= 100 && options.MaxAdvanceDays is >= 1 and <= 60
        && options.MinimumLeadMinutes is >= 0 and <= 1440 && options.ReadyHoldHours is >= 1 and <= 336
        && options.PendingHoldHours is >= 1 and <= 336 && options.PickupGraceMinutes is >= 0 and <= 1440
        && options.ExpiringSoonHours is >= 1 and <= 48 && options.ReconcileIntervalMinutes is >= 1 and <= 60
        && options.OpenDays is { Count: >= 1 and <= 7 } && options.OpenDays.All(d => d is >= 0 and <= 6) && options.OpenDays.Distinct().Count() == options.OpenDays.Count
        && options.PickupLocations is { Count: >= 1 and <= 20 } && options.PickupLocations.All(l => l is not null && !string.IsNullOrWhiteSpace(l.Id) && l.Id.Length <= 40
            && l.Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') && !string.IsNullOrWhiteSpace(l.Name) && l.Name.Length <= 150 && l.Address is { Length: <= 300 })
        && options.PickupLocations.Select(l => l.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == options.PickupLocations.Count;
}

public sealed class PickupLocationOption
{
    public string Id { get; set; } = "main";
    public string Name { get; set; } = "Негізгі кітапхана";
    public string Address { get; set; } = "";
}
