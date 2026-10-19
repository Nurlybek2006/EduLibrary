using EduLibrary.Helpers;

namespace EduLibrary.Tests;

public sealed class ReservationRulesTests
{
    private static readonly DateTime MondayUtc = new(2026, 10, 26, 4, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Defaults_are_valid_and_timezone_round_trip_preserves_calendar()
    {
        Assert.True(ReservationOptions.IsValid(new()));
        var local = ReservationSchedule.ToLocal(MondayUtc);
        Assert.Equal(new DateTime(2026, 10, 26, 9, 0, 0), local);
        Assert.Equal(MondayUtc, ReservationSchedule.ToUtc(local));
    }

    [Theory]
    [InlineData("09:00", false)]
    [InlineData("09:30", true)]
    [InlineData("09:07", false)]
    [InlineData("17:30", true)]
    [InlineData("18:00", false)]
    [InlineData("08:30", false)]
    [InlineData("9:30", false)]
    [InlineData("25:00", false)]
    public void Slots_enforce_opening_grid_last_appointment_and_inclusive_lead(string time, bool expected)
    {
        Assert.Equal(expected, ReservationSchedule.TryGetSlot(new DateTime(2026, 10, 26), time, MondayUtc, new(), out _));
    }

    [Theory]
    [InlineData(2026, 10, 25)]
    [InlineData(2026, 11, 1)]
    [InlineData(2026, 11, 10)]
    public void Past_closed_and_beyond_horizon_dates_are_rejected(int year, int month, int day)
    {
        Assert.False(ReservationSchedule.TryGetSlot(new DateTime(year, month, day), "10:00", MondayUtc, new(), out _));
    }

    [Fact]
    public void Enumerated_slots_are_future_open_and_within_horizon()
    {
        var options = new ReservationOptions { MaxAdvanceDays = 1 };
        var slots = ReservationSchedule.EnumerateSlots(MondayUtc, options).ToArray();
        Assert.Equal(35, slots.Length); // 17 Monday appointments after lead + 18 Tuesday.
        Assert.Equal(MondayUtc.AddMinutes(30), slots[0]);
        Assert.All(slots, slot => {
            var local = ReservationSchedule.ToLocal(slot);
            Assert.True(ReservationSchedule.IsConfiguredSlot(local, options));
            Assert.InRange(slot, MondayUtc.AddMinutes(30), ReservationSchedule.ToUtc(new DateTime(2026, 10, 27, 17, 30, 0)));
        });
        Assert.Equal(slots.Length, slots.Distinct().Count());
    }

    [Fact]
    public void Hold_deadline_honors_both_hold_duration_and_future_appointment_grace()
    {
        var options = new ReservationOptions();
        Assert.Equal(MondayUtc.AddHours(48), ReservationSchedule.HoldDeadline(MondayUtc, MondayUtc.AddHours(1), 48, options));
        Assert.Equal(MondayUtc.AddDays(3).AddHours(1), ReservationSchedule.HoldDeadline(MondayUtc, MondayUtc.AddDays(3), 24, options));
    }

    [Fact]
    public void Configuration_rejects_invalid_hours_capacity_days_and_duplicate_locations()
    {
        Assert.False(ReservationOptions.IsValid(new() { ClosingTime = "08:30" }));
        Assert.False(ReservationOptions.IsValid(new() { SlotCapacity = 0 }));
        Assert.False(ReservationOptions.IsValid(new() { OpenDays = [1, 1] }));
        Assert.False(ReservationOptions.IsValid(new() { PickupLocations = [new() { Id = "bad/id", Name = "Desk" }] }));
        Assert.False(ReservationOptions.IsValid(new() { PickupLocations = [new() { Id = "same", Name = "A" }, new() { Id = "same", Name = "B" }] }));
    }

    [Fact]
    public void Pickup_codes_are_128_bit_opaque_unique_and_normalized_without_accepting_malformed_values()
    {
        var codes = Enumerable.Range(0, 1024).Select(_ => ReservationPickupCode.Generate()).ToArray();
        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.Matches("^ELR-[0-9A-F]{32}$", code));
        var sample = codes[0];
        Assert.True(ReservationPickupCode.Matches("  " + sample.ToLowerInvariant() + "  ", sample));
        Assert.False(ReservationPickupCode.Matches(codes[1], sample));
        Assert.False(ReservationPickupCode.Matches(null, sample));
        Assert.Null(ReservationPickupCode.Normalize("ELR-" + new string('G', 32)));
        Assert.Null(ReservationPickupCode.Normalize(new string('A', 101)));
        Assert.Null(ReservationPickupCode.Normalize("/Reservations/Details/123"));
    }
}
