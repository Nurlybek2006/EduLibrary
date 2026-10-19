using System.Globalization;
using EduLibrary.Data;
using EduLibrary.Data.Seed;
using EduLibrary.Helpers;
using EduLibrary.Models.Entities;
using EduLibrary.Services.Interfaces;
using EduLibrary.Services.Implementations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsDevelopment())
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
        .AddEnvironmentVariables().AddCommandLine(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection мәнін орнатыңыз. README.md файлын қараңыз.");
builder.Services.AddDbContext<ApplicationDbContext>(o=>o.UseSqlServer(connectionString,
    sql=>sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o=>
{
    o.User.RequireUniqueEmail = true;
    o.Password.RequiredLength = 10;
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddEntityFrameworkStores<ApplicationDbContext>().AddErrorDescriber<KazakhIdentityErrorDescriber>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(o=>
{
    o.LoginPath = "/Account/Login";
    o.AccessDeniedPath = "/Account/AccessDenied";
    o.Cookie.Name = "EduLibrary.Identity";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
});
builder.Services.Configure<SecurityStampValidatorOptions>(o=>o.ValidationInterval = TimeSpan.Zero);
builder.Services.AddOptions<LibraryOptions>().BindConfiguration("Library")
    .Validate(o=>o.FinePerOverdueDay >= 0 && o.FinePerOverdueDay <= 100000 && decimal.Round(o.FinePerOverdueDay,2)==o.FinePerOverdueDay && o.DefaultLoanDays is >=1 and <=365, "Кітапхана саясаты жарамсыз.").ValidateOnStart();
builder.Services.AddSingleton<ILibraryClock, LibraryClock>();
builder.Services.AddScoped<ICatalogService, CatalogService>();
builder.Services.AddScoped<ICirculationService, CirculationService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IStudentService, StudentService>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddControllersWithViews(o=>
{
    o.ModelBinderProviders.Insert(0, new DecimalModelBinderProvider());
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    o.ModelBindingMessageProvider.SetValueMustNotBeNullAccessor(_=>"Бұл өрісті толтырыңыз.");
    o.ModelBindingMessageProvider.SetValueIsInvalidAccessor(_=>"Енгізілген мән жарамсыз.");
    o.ModelBindingMessageProvider.SetAttemptedValueIsInvalidAccessor((_,name)=>$"«{name}» өрісінің мәні жарамсыз.");
    o.ModelBindingMessageProvider.SetValueMustBeANumberAccessor(name=>$"«{name}» өрісіне сан енгізіңіз.");
    o.ModelBindingMessageProvider.SetMissingBindRequiredValueAccessor(name=>$"«{name}» өрісін толтырыңыз.");
    o.ModelBindingMessageProvider.SetNonPropertyAttemptedValueIsInvalidAccessor(_=>"Енгізілген мән жарамсыз.");
    o.ModelBindingMessageProvider.SetNonPropertyValueMustBeANumberAccessor(()=>"Сан енгізіңіз.");
});
builder.Services.Configure<RequestLocalizationOptions>(o=>
{
    var culture = new CultureInfo("kk-KZ");
    o.DefaultRequestCulture = new(culture);
    o.SupportedCultures = [culture]; o.SupportedUICultures = [culture];
});
var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    try
    {
        if (builder.Configuration.GetValue<bool>("Database:ApplyMigrations")) await db.Database.MigrateAsync();
        await DbSeeder.SeedAsync(scope.ServiceProvider, builder.Configuration, app.Environment.IsDevelopment());
    }
    catch (Exception ex)
    {
        app.Logger.LogCritical(ex, "EduLibraryDb базасына қосылу орындалмады. ConnectionStrings:DefaultConnection және SQL Server рұқсаттарын тексеріңіз."); throw;
    }
}
if (args.Contains("--initialize-db")) return;
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Home/Error"); app.UseHsts(); app.UseHttpsRedirection(); }
app.UseStatusCodePagesWithReExecute("/Home/Status", "?code={0}");
app.Use(async (context,next)=>
{
    context.Response.Headers.XContentTypeOptions = "nosniff"; context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin"; await next();
});
app.UseRequestLocalization(); app.UseStaticFiles(); app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.Run();
public partial class Program;
