using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using EduLibrary.Helpers;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;

namespace EduLibrary.Tests;

public sealed class AvatarCropBindingTests
{
    [Theory]
    [InlineData("kk-KZ", false)]
    [InlineData("ru-RU", false)]
    [InlineData("en-US", true)]
    public void Native_culture_parser_demonstrates_dot_fraction_regression(string culture, bool expected)
        => Assert.Equal(expected, double.TryParse("0.25", NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.GetCultureInfo(culture), out _));

    [Theory]
    [InlineData("kk-KZ", "0.2500000000")]
    [InlineData("kk-KZ", "0,2500000000")]
    [InlineData("ru-RU", "0.2500000000")]
    [InlineData("ru-RU", "0,2500000000")]
    [InlineData("en-US", "0.2500000000")]
    [InlineData("en-US", "0,2500000000")]
    public async Task Dot_and_comma_fractions_bind_identically_in_all_UI_cultures(string culture, string input)
    {
        var context = await BindAsync(culture, input);
        Assert.True(context.Result.IsModelSet);
        Assert.Equal(.25, Assert.IsType<double>(context.Result.Model));
        Assert.Equal(0, context.ModelState.ErrorCount);
        Assert.Equal(input, context.ModelState[nameof(AvatarUploadViewModel.CropX)]!.AttemptedValue);
    }

    [Theory]
    [InlineData("kk-KZ")]
    [InlineData("ru-RU")]
    [InlineData("en-US")]
    public async Task Nonfinite_grouped_exponent_and_oversize_payloads_are_rejected(string culture)
    {
        foreach (var input in new[] { "NaN", "Infinity", "-Infinity", "∞", "1e999", "1e-2", "1,000.25", "1.000,25", "1 000", "1,000,000", "", new string('9', 400) })
        {
            var context = await BindAsync(culture, input);
            Assert.False(context.Result.IsModelSet, input);
            Assert.False(context.ModelState.IsValid, input);
            Assert.Single(context.ModelState[nameof(AvatarUploadViewModel.CropX)]!.Errors);
        }
    }

    [Theory]
    [InlineData("kk-KZ")]
    [InlineData("ru-RU")]
    [InlineData("en-US")]
    public async Task Range_and_image_crop_guards_still_reject_outside_unit_interval(string culture)
    {
        var range = typeof(AvatarUploadViewModel).GetProperty(nameof(AvatarUploadViewModel.CropX))!.GetCustomAttribute<RangeAttribute>()!;
        foreach (var input in new[] { "-0.01", "1.01", "10000000000000000000" })
        {
            var context = await BindAsync(culture, input);
            var number = Assert.IsType<double>(context.Result.Model);
            Assert.False(range.IsValid(number));
            Assert.False(AvatarImageProcessor.ValidCrop(new() { CropX = number, CropSize = .5 }));
        }
    }

    [Fact]
    public async Task Duplicate_crop_values_are_rejected_instead_of_selecting_one()
    {
        var context = await BindAsync("kk-KZ", new StringValues(["0.25", "0.75"]));
        Assert.False(context.Result.IsModelSet);
        Assert.False(context.ModelState.IsValid);
    }

    [Fact]
    public void Binder_is_scoped_to_each_crop_property_and_retains_range_validation()
    {
        foreach (var propertyName in new[] { nameof(AvatarUploadViewModel.CropX), nameof(AvatarUploadViewModel.CropY), nameof(AvatarUploadViewModel.CropSize) })
        {
            var property = typeof(AvatarUploadViewModel).GetProperty(propertyName)!;
            Assert.Equal(typeof(DoubleModelBinder), property.GetCustomAttribute<ModelBinderAttribute>()!.BinderType);
            Assert.NotNull(property.GetCustomAttribute<RangeAttribute>());
        }
        Assert.Null(typeof(ProfileViewModel).GetProperty(nameof(ProfileViewModel.FullName))!.GetCustomAttribute<ModelBinderAttribute>());
    }

    private static async Task<ModelBindingContext> BindAsync(string culture, StringValues input)
    {
        var name = nameof(AvatarUploadViewModel.CropX);
        var values = new FormValueProvider(BindingSource.Form,
            new FormCollection(new Dictionary<string, StringValues> { [name] = input }), CultureInfo.GetCultureInfo(culture));
        var context = DefaultModelBindingContext.CreateBindingContext(
            new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor(), new ModelStateDictionary()),
            values, new EmptyModelMetadataProvider().GetMetadataForType(typeof(double)), null, name);
        await new DoubleModelBinder().BindModelAsync(context);
        return context;
    }
}
