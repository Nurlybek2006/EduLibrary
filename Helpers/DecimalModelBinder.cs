using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace EduLibrary.Helpers;

// HTML number inputs submit a decimal point even when the display culture uses a
// comma. Accept either separator without accepting ambiguous thousands grouping.
public sealed class DecimalModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext context)
    {
        var value = context.ValueProvider.GetValue(context.ModelName);
        if (value == ValueProviderResult.None) return Task.CompletedTask;
        context.ModelState.SetModelValue(context.ModelName, value);
        var text = value.FirstValue?.Trim();
        if (string.IsNullOrEmpty(text) && Nullable.GetUnderlyingType(context.ModelType) is not null)
            context.Result = ModelBindingResult.Success(null);
        else if (decimal.TryParse(text?.Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                     CultureInfo.InvariantCulture, out var amount))
            context.Result = ModelBindingResult.Success(amount);
        else
            context.ModelState.TryAddModelError(context.ModelName, "Жарамды сан енгізіңіз (мысалы, 1500,50).");
        return Task.CompletedTask;
    }
}

public sealed class DecimalModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
        => (Nullable.GetUnderlyingType(context.Metadata.ModelType) ?? context.Metadata.ModelType) == typeof(decimal)
            ? new DecimalModelBinder() : null;
}
