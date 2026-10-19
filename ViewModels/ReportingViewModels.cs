using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace EduLibrary.ViewModels;

public sealed class ReportFilterViewModel
{
    public string Type { get; set; } = "loans";
    public string Range { get; set; } = "30";
    [DataType(DataType.Date), Display(Name = "Басталу күні")]
    public DateTime? StartDate { get; set; }
    [DataType(DataType.Date), Display(Name = "Аяқталу күні")]
    public DateTime? EndDate { get; set; }
    [StringLength(200, ErrorMessage = "Іздеу мәтіні 200 таңбадан аспауы тиіс.")]
    public string? Search { get; set; }
    public string Status { get; set; } = "all";
    public int? CategoryId { get; set; }
    public string? Faculty { get; set; }
    public string Sort { get; set; } = "default";
    public string Direction { get; set; } = "desc";
    public string GroupBy { get; set; } = "day";
    public int Page { get; set; } = 1;
}

public sealed record ReportMetricViewModel(string Label, string Value, string? Hint = null);
public sealed record ReportColumnViewModel(string Label, string Sort);
public sealed record ReportCellViewModel(string Value, string? Controller = null, string? Action = null, int? Id = null);
public sealed class ReportRowViewModel
{
    public IReadOnlyList<ReportCellViewModel> Cells { get; set; } = [];
}

public sealed class ReportPageViewModel
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public ReportFilterViewModel Filter { get; set; } = new();
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public DateTime GeneratedAt { get; set; }
    public IReadOnlyList<ReportMetricViewModel> Summary { get; set; } = [];
    public IReadOnlyList<ReportColumnViewModel> Columns { get; set; } = [];
    public IReadOnlyList<ReportRowViewModel> Rows { get; set; } = [];
    public IReadOnlyList<SelectListItem> Categories { get; set; } = [];
    public IReadOnlyList<SelectListItem> Faculties { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageSize { get; set; } = 20;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    public Dictionary<string, string> RouteValues(string? type = null, string? sort = null, int? page = null)
        => new Dictionary<string, string>
        {
            ["Type"] = type ?? Filter.Type, ["Range"] = Filter.Range,
            ["StartDate"] = StartDate.ToString("yyyy-MM-dd"), ["EndDate"] = EndDate.ToString("yyyy-MM-dd"),
            ["Search"] = Filter.Search ?? "", ["Status"] = Filter.Status,
            ["CategoryId"] = Filter.CategoryId?.ToString() ?? "", ["Faculty"] = Filter.Faculty ?? "",
            ["Sort"] = type is not null ? "default" : sort ?? Filter.Sort,
            ["Direction"] = type is not null ? "desc" : sort is null ? Filter.Direction : (Filter.Sort == sort && Filter.Direction == "desc" ? "asc" : "desc"),
            ["GroupBy"] = Filter.GroupBy, ["Page"] = (page ?? (sort is not null || type is not null ? 1 : Filter.Page)).ToString()
        };
}

public sealed class AnalyticsChartViewModel
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Type { get; set; } = "line";
    public string[] Labels { get; set; } = [];
    public double[] Values { get; set; } = [];
    public bool Horizontal { get; set; }
    public bool Fill { get; set; }
    public bool IsRating { get; set; }
    public bool HasData => Values.Any(x => x > 0);
}

public sealed class AnalyticsViewModel
{
    public ReportFilterViewModel Filter { get; set; } = new();
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public DateTime Today { get; set; }
    public IReadOnlyList<ReportMetricViewModel> Statistics { get; set; } = [];
    public IReadOnlyList<AnalyticsChartViewModel> Charts { get; set; } = [];
}
