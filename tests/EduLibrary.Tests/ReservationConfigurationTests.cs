using System.Text;
using EduLibrary.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EduLibrary.Tests;

public sealed class ReservationConfigurationTests
{
    private static ConfigurationManager Configuration(string json)
    {
        var configuration = new ConfigurationManager();
        configuration.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)));
        return configuration;
    }

    [Fact]
    public void Runtime_options_binding_replaces_seeded_collections_and_passes_validation()
    {
        // The former BindConfiguration registration appended to both initialized
        // lists and made the real host fail startup validation.
        using var configuration = Configuration("""
            { "Reservations": {
                "OpenDays": [1, 2, 3, 4, 5],
                "PickupLocations": [{ "Id": "main", "Name": "Негізгі кітапхана", "Address": "Бірінші қабат" }],
                "SlotCapacity": 7
            } }
            """);
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOptions<ReservationOptions>()
            .Configure<IConfiguration>(ReservationOptions.Bind)
            .Validate(ReservationOptions.IsValid);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<ReservationOptions>>().Value;

        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, options.OpenDays);
        var location = Assert.Single(options.PickupLocations);
        Assert.Equal("main", location.Id);
        Assert.Equal("Бірінші қабат", location.Address);
        Assert.Equal(7, options.SlotCapacity);
        Assert.True(ReservationOptions.IsValid(options));
    }

    [Fact]
    public void Missing_collection_keys_preserve_safe_defaults_when_other_settings_are_bound()
    {
        using var configuration = Configuration("""{ "Reservations": { "SlotCapacity": 3, "ReadyHoldHours": 72 } }""");
        var options = new ReservationOptions();

        ReservationOptions.Bind(options, configuration);

        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, options.OpenDays);
        Assert.Equal("main", Assert.Single(options.PickupLocations).Id);
        Assert.Equal(3, options.SlotCapacity);
        Assert.Equal(72, options.ReadyHoldHours);
        Assert.True(ReservationOptions.IsValid(options));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Replacing_one_configured_collection_retains_the_other_collection_default(bool configureDays)
    {
        using var configuration = Configuration(configureDays
            ? """{ "Reservations": { "OpenDays": [6] } }"""
            : """{ "Reservations": { "PickupLocations": [{ "Id": "hall", "Name": "Оқу залы" }] } }""");
        var options = new ReservationOptions();

        ReservationOptions.Bind(options, configuration);

        Assert.Equal(configureDays ? new[] { 6 } : new[] { 1, 2, 3, 4, 5 }, options.OpenDays);
        Assert.Equal(configureDays ? "main" : "hall", Assert.Single(options.PickupLocations).Id);
        Assert.True(ReservationOptions.IsValid(options));
    }

    [Fact]
    public void Configured_collections_replace_defaults_independently_and_rebinding_does_not_append()
    {
        using var configuration = Configuration("""
            { "Reservations": { "OpenDays": [2, 4],
                "PickupLocations": [{ "Id": "reading-room", "Name": "Оқу залы" }] } }
            """);
        var options = new ReservationOptions();

        ReservationOptions.Bind(options, configuration);
        ReservationOptions.Bind(options, configuration);

        Assert.Equal(new[] { 2, 4 }, options.OpenDays);
        Assert.Equal("reading-room", Assert.Single(options.PickupLocations).Id);
        Assert.DoesNotContain(options.PickupLocations, location => location.Id == "main");
        Assert.True(ReservationOptions.IsValid(options));
    }

    [Theory]
    [InlineData("""{ "Reservations": { "OpenDays": [1, 1] } }""")]
    [InlineData("""{ "Reservations": { "PickupLocations": [{ "Id": "same", "Name": "A" }, { "Id": "SAME", "Name": "B" }] } }""")]
    public void Binding_preserves_invalid_configured_duplicates_for_validation_to_reject(string json)
    {
        using var configuration = Configuration(json);
        var options = new ReservationOptions();

        ReservationOptions.Bind(options, configuration);

        Assert.False(ReservationOptions.IsValid(options));
    }

    [Fact]
    public void Missing_reservations_section_keeps_complete_valid_fallback()
    {
        using var configuration = Configuration("""{ "OtherFeature": { "Enabled": true } }""");
        var options = new ReservationOptions();

        ReservationOptions.Bind(options, configuration);

        Assert.True(ReservationOptions.IsValid(options));
        Assert.Equal(5, options.OpenDays.Count);
        Assert.Single(options.PickupLocations);
        Assert.Equal(48, options.ReadyHoldHours);
    }
}
