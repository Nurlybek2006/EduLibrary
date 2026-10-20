using EduLibrary.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace EduLibrary.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return User.IsInRole("Admin") || User.IsInRole("Librarian")
                ? RedirectToAction("Index", "Dashboard") : RedirectToAction("Index", "Books");
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        [IgnoreAntiforgeryToken]
        [ResponseCache(Duration=0, Location=ResponseCacheLocation.None, NoStore=true)]
        public IActionResult Status(int code = 404)
        {
            Response.StatusCode = code is >=400 and <=599 ? code : 404;
            var (title, message) = Response.StatusCode switch
            {
                403 => (global::EduLibrary.Localization.UiText.Get("Қолжетімділік шектелген"), global::EduLibrary.Localization.UiText.Get("Бұл әрекетті орындауға рұқсатыңыз жоқ.")),
                400 => (global::EduLibrary.Localization.UiText.Get("Сұрау жарамсыз"), global::EduLibrary.Localization.UiText.Get("Бетті жаңартып, әрекетті қайталап көріңіз.")),
                404 => (global::EduLibrary.Localization.UiText.Get("Бет табылмады"), global::EduLibrary.Localization.UiText.Get("Сілтеме ескірген немесе бұл жазба қолжетімсіз.")),
                _ => (global::EduLibrary.Localization.UiText.Get("Сұрау орындалмады"), global::EduLibrary.Localization.UiText.Get("Кейінірек қайталап көріңіз немесе кітапхана әкімшісіне хабарласыңыз."))
            };
            return View(new EduLibrary.ViewModels.StatusPageViewModel(Response.StatusCode, title, message));
        }

        [Microsoft.AspNetCore.Authorization.Authorize]
        public IActionResult Intelligence(string section = "recommendations")
        {
            return RedirectToAction("Index",section switch {"analytics"=>"Analytics","reports"=>"Reports",_=>"Recommendations"});
        }

        [Microsoft.AspNetCore.Authorization.Authorize(Roles="Admin")]
        public IActionResult Settings([FromServices] Microsoft.Extensions.Options.IOptions<EduLibrary.Helpers.LibraryOptions> options)
            => View(options.Value);
    }
}
