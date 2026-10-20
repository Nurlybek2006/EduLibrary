using System.Globalization;
using EduLibrary;
using EduLibrary.Controllers;
using EduLibrary.Data;
using EduLibrary.Localization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Localization;

namespace EduLibrary.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData("kk-KZ", "Кітаптар каталогы", "Сақтау")]
    [InlineData("ru-RU", "Каталог книг", "Сохранить")]
    [InlineData("en-US", "Book catalog", "Save")]
    public void StandardLocalizerAndDomainMessagesUseTheSameResources(string culture,string catalog,string save)
    {
        using var scope=new CultureScope(culture);
        var services=new ServiceCollection();services.AddLogging();services.AddLocalization(o=>o.ResourcesPath="Resources");
        using var provider=services.BuildServiceProvider();
        var localizer=provider.GetRequiredService<IStringLocalizer<SharedResource>>();
        Assert.Equal(catalog,localizer["Кітаптар каталогы"].Value);
        Assert.Equal(save,UiText.Get("Сақтау"));
        Assert.False(localizer["Кітаптар каталогы"].ResourceNotFound);
        Assert.DoesNotContain("Recommendations.",UiText.Get("Recommendations.FacultyLoans",3));
        Assert.Contains("3",UiText.Get("Recommendations.FacultyLoans",3));
    }
    [Fact]
    public void UnsupportedCultureFallsBackToKazakhAndUnknownContentIsPreserved()
    {
        using var scope=new CultureScope("fr-FR");
        Assert.Equal("Сақтау",UiText.Get("Сақтау"));
        Assert.Equal("Clean Code",UiText.Get("Clean Code"));
    }
    [Theory]
    [InlineData("kk-KZ")][InlineData("ru-RU")][InlineData("en-US")]
    public async Task CultureCookiePersistsAndLocalReturnUrlRetainsQuery(string culture)
    {
        await using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer("Server=.;Database=NotOpened;Integrated Security=true").Options);
        var http=new DefaultHttpContext();
        var action=new ActionContext(http,new RouteData(),new ControllerActionDescriptor());
        var controller=new CultureController(db,new TestEnvironment()) { ControllerContext=new ControllerContext(action),Url=new UrlHelper(action) };
        var result=Assert.IsType<LocalRedirectResult>(await controller.Set(culture,"/Books?Search=SQL&Page=2",default));
        Assert.Equal("/Books?Search=SQL&Page=2",result.Url);
        var cookie=http.Response.Headers.SetCookie.ToString();
        Assert.Contains(CookieRequestCultureProvider.DefaultCookieName,cookie);
        Assert.Contains(Uri.EscapeDataString("c="+culture+"|uic="+culture),cookie,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly",cookie,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax",cookie,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=",cookie,StringComparison.OrdinalIgnoreCase);
    }
    [Theory]
    [InlineData("https://example.com")][InlineData("//example.com")][InlineData("/\\example.com")]
    public async Task CultureSwitchRejectsExternalReturnUrls(string returnUrl)
    {
        await using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer("Server=.;Database=NotOpened;Integrated Security=true").Options);
        var action=new ActionContext(new DefaultHttpContext(),new RouteData(),new ControllerActionDescriptor());
        var controller=new CultureController(db,new TestEnvironment()) {ControllerContext=new ControllerContext(action),Url=new UrlHelper(action)};
        Assert.Equal("/Books",Assert.IsType<LocalRedirectResult>(await controller.Set("en-US",returnUrl,default)).Url);
        Assert.IsType<BadRequestResult>(await controller.Set("invalid","/Books",default));
    }
    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; }="Development";
        public string ApplicationName { get; set; }="EduLibrary";
        public string WebRootPath { get; set; }="";
        public IFileProvider WebRootFileProvider { get; set; }=new NullFileProvider();
        public string ContentRootPath { get; set; }="";
        public IFileProvider ContentRootFileProvider { get; set; }=new NullFileProvider();
    }
}
public sealed class CultureScope : IDisposable
{
    private readonly CultureInfo culture=CultureInfo.CurrentCulture,ui=CultureInfo.CurrentUICulture;
    public CultureScope(string name) { CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(name);CultureInfo.CurrentUICulture=CultureInfo.GetCultureInfo(name); }
    public void Dispose() {CultureInfo.CurrentCulture=culture;CultureInfo.CurrentUICulture=ui;}
}
