using System.Security.Claims;
using System.Text;
using EduLibrary.Helpers;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduLibrary.Controllers;

[Authorize(Roles = RoleNames.Staff + "," + RoleNames.Student)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ReservationsController(IReservationService reservations) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    private bool IsStaff => User.IsInRole(RoleNames.Admin) || User.IsInRole(RoleNames.Librarian);

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] ReservationFilterViewModel filter, CancellationToken ct) =>
        View(await reservations.GetListAsync(filter, UserId, IsStaff, ct));

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var model = await reservations.GetDetailsAsync(id, UserId, IsStaff, ct);
        return model is null ? NotFound() : View(model);
    }

    [HttpGet, Authorize(Roles = RoleNames.Student)]
    public async Task<IActionResult> Create(int bookId, DateTime? pickupDate, string? pickupLocationId, CancellationToken ct)
    {
        ReservationCreateViewModel? model;
        try { model = await reservations.GetCreateFormAsync(bookId, UserId, ct); }
        catch (BusinessException ex) { return IneligibleStudent(ex, bookId); }
        if (model is null) return NotFound();
        if (!model.JoinQueue && pickupDate.HasValue && model.PickupDates.Any(day => day.Value == pickupDate.Value.ToString("yyyy-MM-dd"))
            && model.Locations.Any(location => location.Value == pickupLocationId))
        {
            model.PickupDate = pickupDate.Value.Date;
            model.PickupLocationId = pickupLocationId!;
            model.PickupTime = null;
            model.Slots = await reservations.GetSlotsAsync(model.PickupDate.Value, model.PickupLocationId, ct);
        }
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = RoleNames.Student)]
    public async Task<IActionResult> Create(ReservationCreateViewModel model, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var id = await reservations.CreateAsync(model, UserId, ct);
                TempData["Success"] = global::EduLibrary.Localization.UiText.Get("Өтініміңіз сақталды. Бронь мәртебесі мен хабарландыруларды бақылаңыз.");
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); }
        }
        ReservationCreateViewModel? form;
        try { form = await reservations.GetCreateFormAsync(model.BookId, UserId, ct); }
        catch (BusinessException ex) { return IneligibleStudent(ex, model.BookId); }
        if (form is null) return NotFound();
        form.PickupDate = model.PickupDate;
        form.PickupLocationId = model.PickupLocationId;
        form.PickupTime = model.PickupTime;
        if (model.PickupDate.HasValue && !string.IsNullOrWhiteSpace(model.PickupLocationId))
        {
            try { form.Slots = await reservations.GetSlotsAsync(model.PickupDate.Value, model.PickupLocationId, ct); }
            catch (BusinessException) { form.Slots = []; }
        }
        return View(form);
    }

    [HttpGet]
    public async Task<IActionResult> Slots(DateTime? pickupDate, string? pickupLocationId, CancellationToken ct)
    {
        if (!ModelState.IsValid || !pickupDate.HasValue || string.IsNullOrWhiteSpace(pickupLocationId))
            return BadRequest(new { message = global::EduLibrary.Localization.UiText.Get("Алып кету орны мен күнін таңдаңыз.") });
        try { return Json(await reservations.GetSlotsAsync(pickupDate.Value, pickupLocationId, ct)); }
        catch (BusinessException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet, Authorize(Roles = RoleNames.Staff)]
    public async Task<IActionResult> Prepare(int id, CancellationToken ct)
    {
        var model = await reservations.GetPrepareFormAsync(id, UserId, ct);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = RoleNames.Staff)]
    public async Task<IActionResult> MarkReady(ReservationReadyViewModel model, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try { await reservations.MarkReadyAsync(model, UserId, ct); TempData["Success"] = global::EduLibrary.Localization.UiText.Get("Кітап алып кетуге дайын. Студентке хабарлама жіберілді."); return RedirectToAction(nameof(Details), new { id = model.Id }); }
            catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); }
        }
        var form = await reservations.GetPrepareFormAsync(model.Id, UserId, ct);
        if (form is null) return NotFound();
        form.BookCopyId = model.BookCopyId;
        return View("Prepare", form);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = RoleNames.Staff)]
    public async Task<IActionResult> Fulfill(ReservationFulfillViewModel model, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var loanId = await reservations.FulfillAsync(model, UserId, ct);
                TempData["Success"] = global::EduLibrary.Localization.UiText.Get("Кітап студентке берілді. Бронь орындалып, кітап беру тарихы жаңартылды.");
                return RedirectToAction("Details", "Loans", new { id = loanId });
            }
            catch (BusinessException ex) { ModelState.AddModelError("", ex.Message); }
        }
        var details = await reservations.GetDetailsAsync(model.Id, UserId, true, ct);
        if (details is null) return NotFound();
        return View("Details", details);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, string rowVersion, CancellationToken ct)
    {
        try { await reservations.CancelAsync(id, rowVersion, UserId, IsStaff, ct); TempData["Success"] = global::EduLibrary.Localization.UiText.Get("Бронь тоқтатылды. Бөлінген дана кезек тәртібіне сай босатылады."); }
        catch (BusinessException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet, Authorize(Roles = RoleNames.Staff)]
    public IActionResult Lookup() => View(new ReservationCodeLookupViewModel());

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = RoleNames.Staff)]
    public async Task<IActionResult> Lookup(ReservationCodeLookupViewModel model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.Code)) ModelState.AddModelError(nameof(model.Code), global::EduLibrary.Localization.UiText.Get("Студент көрсеткен бронь кодын енгізіңіз."));
        if (!ModelState.IsValid) return View(model);
        try
        {
            var id = await reservations.FindByCodeAsync(model.Code!, UserId, ct);
            if (id.HasValue) return RedirectToAction(nameof(Details), new { id = id.Value });
            ModelState.AddModelError(nameof(model.Code), global::EduLibrary.Localization.UiText.Get("Бұл кодқа сәйкес бронь табылмады. Кодты тексеріңіз."));
        }
        catch (BusinessException ex) { ModelState.AddModelError(nameof(model.Code), ex.Message); }
        return View(model);
    }

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Qr(int id, CancellationToken ct)
    {
        var model = await reservations.GetDetailsAsync(id, UserId, IsStaff, ct);
        if (model is null || string.IsNullOrWhiteSpace(model.PickupCode)) return NotFound();
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
        return Content(CopyCode.CreateQrSvg(model.PickupCode), "image/svg+xml", Encoding.UTF8);
    }

    private RedirectToActionResult IneligibleStudent(BusinessException error, int bookId)
    {
        TempData["Error"] = error.Message;
        return RedirectToAction("Details", "Books", new { id = bookId });
    }
}
