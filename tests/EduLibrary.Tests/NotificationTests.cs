using EduLibrary.Services.Implementations;

namespace EduLibrary.Tests;

public sealed class NotificationTests
{
    [Theory]
    [InlineData(-15,"Overdue")]
    [InlineData(-1,"Overdue")]
    [InlineData(0,null)]
    [InlineData(1,"Due1")]
    [InlineData(2,null)]
    [InlineData(3,"Due3")]
    [InlineData(4,null)]
    public void RemindersFollowLibraryCalendarDays(int days,string? expected)
    {
        var today=new DateTime(2026,10,20);
        Assert.Equal(expected,NotificationRules.ReminderType(today.AddDays(days),today));
    }
    [Fact]
    public void ReminderKeyIsStableButDifferentForLoanDateAndKind()
    {
        var day=new DateTime(2026,10,20);
        var key=NotificationRules.EventKey("Due1",42,day);
        Assert.Equal(key,NotificationRules.EventKey("Due1",42,day.AddHours(14)));
        Assert.NotEqual(key,NotificationRules.EventKey("Overdue",42,day));
        Assert.NotEqual(key,NotificationRules.EventKey("Due1",43,day));
        Assert.NotEqual(key,NotificationRules.EventKey("Due1",42,day.AddDays(1)));
    }
}
