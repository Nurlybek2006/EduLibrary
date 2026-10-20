using System.Globalization;
using EduLibrary.Localization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace EduLibrary.Helpers;

// HTML crop decimals must not depend on UI culture. Separators denote
// fractions, never thousands groups; exponent and nonfinite values are invalid.
public sealed class DoubleModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext context)
    {
        var value = context.ValueProvider.GetValue(context.ModelName);
        if (value == ValueProviderResult.None) return Task.CompletedTask;
        context.ModelState.SetModelValue(context.ModelName, value);
        var text = value.FirstValue?.Trim();
        if (value.Length == 1 && text is { Length: > 0 and <= 64 }
            && double.TryParse(text.Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var number) && double.IsFinite(number))
            context.Result = ModelBindingResult.Success(number);
        else
            context.ModelState.TryAddModelError(context.ModelName, UiText.Get("The photo crop is invalid."));
        return Task.CompletedTask;
    }
}
