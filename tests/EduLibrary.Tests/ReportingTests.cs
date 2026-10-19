using System.Text;
using EduLibrary.Helpers;
using EduLibrary.Services.Implementations;
using EduLibrary.ViewModels;

namespace EduLibrary.Tests;

public sealed class ReportingTests
{
    private static readonly DateTime Today = new(2026, 10, 20);

    [Theory]
    [InlineData("7", 7)]
    [InlineData("30", 30)]
    [InlineData("90", 90)]
    public void PresetPeriodIncludesTodayWithoutAnExtraDay(string range, int days)
    {
        var period = ReportingRules.ResolvePeriod(new() { Range = range }, Today);
        Assert.Equal(days, (period.EndExclusive - period.Start).Days);
        Assert.Equal(Today, period.End);
        Assert.Equal(Today.AddDays(1), period.EndExclusive);
    }

    [Fact]
    public void ThisYearStartsAtJanuaryFirst()
    {
        var period = ReportingRules.ResolvePeriod(new() { Range = "year" }, Today);
        Assert.Equal(new DateTime(2026, 1, 1), period.Start);
        Assert.Equal(Today, period.End);
    }

    [Fact]
    public void LocalCalendarPeriodConvertsToCorrectUtcReviewAndPaymentBounds()
    {
        var period = ReportingRules.ResolvePeriod(new() { Range = "custom", StartDate = Today, EndDate = Today }, Today);
        Assert.Equal(new DateTime(2026, 10, 19, 19, 0, 0, DateTimeKind.Utc), period.UtcStart);
        Assert.Equal(new DateTime(2026, 10, 20, 19, 0, 0, DateTimeKind.Utc), period.UtcEnd);
    }

    [Fact]
    public void CustomLeapDayPeriodRetainsAllCalendarDays()
    {
        var filter = new ReportFilterViewModel { Range = "custom", StartDate = new(2024, 2, 28), EndDate = new(2024, 3, 1) };
        var period = ReportingRules.ResolvePeriod(filter, Today);
        Assert.Equal(3, (period.EndExclusive - period.Start).Days);
    }

    [Theory]
    [InlineData(2026, 10, 21, 2026, 10, 22)]
    [InlineData(2026, 10, 20, 2026, 10, 19)]
    [InlineData(1899, 1, 1, 1900, 1, 1)]
    [InlineData(2000, 1, 1, 2026, 10, 20)]
    public void InvalidCustomRangesAreRejected(int startYear, int startMonth, int startDay, int endYear, int endMonth, int endDay)
    {
        Assert.Throws<BusinessException>(() => ReportingRules.ResolvePeriod(new()
        {
            Range = "custom", StartDate = new(startYear, startMonth, startDay), EndDate = new(endYear, endMonth, endDay)
        }, Today));
    }

    [Fact]
    public void UnknownPresetNormalizesToThirtyDays()
    {
        var filter = new ReportFilterViewModel { Range = "untrusted" };
        var period = ReportingRules.ResolvePeriod(filter, Today);
        Assert.Equal("30", filter.Range);
        Assert.Equal(30, (period.EndExclusive - period.Start).Days);
    }

    [Theory]
    [InlineData(20, null, 0)]
    [InlineData(21, null, 0)]
    [InlineData(15, null, 5)]
    [InlineData(10, 15, 5)]
    [InlineData(15, 12, 0)]
    public void OverdueUsesCalendarDaysAndFreezesOnReturn(int dueDay, int? returnDay, int expected)
    {
        Assert.Equal(expected, ReportingRules.OverdueDays(new(2026, 10, dueDay, 22, 0, 0), returnDay.HasValue ? new DateTime(2026, 10, returnDay.Value, 1, 0, 0) : null, Today));
    }

    [Theory]
    [InlineData("=SUM(A1:A2)")]
    [InlineData("+cmd|' /C calc'!A0")]
    [InlineData("-1+2")]
    [InlineData("@SUM(A1)")]
    [InlineData("  =1+1")]
    [InlineData("\t=1+1")]
    [InlineData("\0=1+1")]
    [InlineData("\r\n+1")]
    public void CsvNeutralizesFormulaPrefixesIncludingWhitespaceAndControls(string value)
        => Assert.StartsWith("\"'", ReportingRules.CsvField(value));

    [Fact]
    public void CsvQuotesCommasQuotesAndNewlinesWithoutLosingText()
    {
        Assert.Equal("\"Кітап, \"\"атауы\"\"\nекінші жол\"", ReportingRules.CsvField("Кітап, \"атауы\"\nекінші жол"));
        Assert.Equal("\"\"", ReportingRules.CsvField(null));
        Assert.Equal("\"Қалыпты атау\"", ReportingRules.CsvField("Қалыпты атау"));
    }

    [Fact]
    public void CsvContainsUtf8BomAndExcelCompatibleRecordSeparators()
    {
        var csv = ReportingRules.CreateCsv([new[] { "Атауы", "Баға" }, new[] { "Қазақ әдебиеті", "5" }]);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, csv.Take(3).ToArray());
        Assert.Equal("\"Атауы\",\"Баға\"\r\n\"Қазақ әдебиеті\",\"5\"\r\n", Encoding.UTF8.GetString(csv, 3, csv.Length - 3));
    }

    [Theory]
    [InlineData(3, 2, 1, 11)]
    [InlineData(0, 0, 5, 0)]
    [InlineData(1, 1, 0, 5)]
    public void ReaderScoreRewardsActualCirculationAndPenalizesOverdue(int loans, int returns, int overdue, int expected)
        => Assert.Equal(expected, ReportingRules.ReaderActivityScore(loans, returns, overdue));

    [Fact]
    public void SortLinksRetainFiltersResetPageAndToggleDirection()
    {
        var report = new ReportPageViewModel
        {
            StartDate = Today.AddDays(-6), EndDate = Today,
            Filter = new() { Type = "popular", Range = "custom", Search = "Алгоритм", CategoryId = 4, Faculty = "Информатика", Sort = "total", Direction = "desc", Page = 3 }
        };
        var route = report.RouteValues(sort: "total");
        Assert.Equal("Алгоритм", route["Search"]);
        Assert.Equal("4", route["CategoryId"]);
        Assert.Equal("Информатика", route["Faculty"]);
        Assert.Equal("1", route["Page"]);
        Assert.Equal("asc", route["Direction"]);
    }
}
