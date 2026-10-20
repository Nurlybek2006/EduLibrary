using System.Globalization;
using EduLibrary.Models.Entities;
using EduLibrary.Models.Enums;
using EduLibrary.Services.Implementations;
using EduLibrary.ViewModels;
using Microsoft.EntityFrameworkCore;

internal static class NotificationLocalizationScenarios
{
    public static async Task RunAsync(DigitalFixture f)
    {
        int issuedId, fineId, queuedId, unknownId, foreignLinkId;
        await using (var db = f.NewDb())
        {
            var reader = await db.Students.SingleAsync(s => s.IdentityUserId == DigitalFixture.Reader);
            var other = await db.Students.SingleAsync(s => s.IdentityUserId == DigitalFixture.OtherReader);
            var ownCopy = await db.BookCopies.FirstAsync(c => c.BookId == f.Books[0]);
            var foreignCopy = await db.BookCopies.FirstAsync(c => c.BookId == f.Books[1]);
            ownCopy.Status = CopyStatus.OnLoan; foreignCopy.Status = CopyStatus.OnLoan;
            var loan = new Loan { StudentId = reader.Id, BookCopyId = ownCopy.Id, LoanDate = f.Clock.Today, DueDate = f.Clock.Today.AddDays(7), IssuedByUserId = DigitalFixture.Staff };
            var foreignLoan = new Loan { StudentId = other.Id, BookCopyId = foreignCopy.Id, LoanDate = f.Clock.Today, DueDate = f.Clock.Today.AddDays(7), IssuedByUserId = DigitalFixture.Staff };
            var reservation = new Reservation { BookId = f.Books[2], StudentId = reader.Id, Status = ReservationStatus.Queued, PickupLocationId = "main", CreatedAtUtc = f.Clock.UtcNow, PickupCode = EduLibrary.Helpers.ReservationPickupCode.Generate() };
            db.Loans.AddRange(loan, foreignLoan); db.Reservations.Add(reservation); await db.SaveChangesAsync();
            var fine = new Fine { LoanId = loan.Id, StudentId = reader.Id, Amount = 200, Reason = "Test", Status = FineStatus.Unpaid, CreatedAt = f.Clock.UtcNow };
            db.Fines.Add(fine); await db.SaveChangesAsync();
            Notification Note(string type, string link, string title, string message) => new() { UserId = DigitalFixture.Reader, Type = type, Link = link, Title = title, Message = message, DeduplicationKey = "DigitalLocalization:" + Guid.NewGuid().ToString("N"), CreatedAt = f.Clock.UtcNow };
            var issued = Note("Issued", $"/Loans/Details/{loan.Id}", "Кітап сәтті берілді", "Ескі қазақша хабар");
            var fineNote = Note("Fine", $"/Fines/Details/{fine.Id}", "Айыппұл есептелді", "Ескі қазақша айыппұл");
            var queued = Note("ReservationQueued", $"/Reservations/Details/{reservation.Id}", "Кітап кезегіне қосылдыңыз", "Ескі қазақша кезек");
            var custom = Note("CustomMessage", "/Notifications", "Original custom title", "Keep this custom content unchanged");
            var foreign = Note("Issued", $"/Loans/Details/{foreignLoan.Id}", "Unmatched source", "Do not resolve another student's book");
            db.Notifications.AddRange(issued, fineNote, queued, custom, foreign); await db.SaveChangesAsync();
            issuedId = issued.Id; fineId = fineNote.Id; queuedId = queued.Id; unknownId = custom.Id; foreignLinkId = foreign.Id;
        }
        var originalCulture = CultureInfo.CurrentCulture; var originalUi = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var culture in new[] { "en-US", "ru-RU", "kk-KZ" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture); CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                await using var db = f.NewDb();
                var page = await new NotificationService(db, f.Clock).GetAsync(DigitalFixture.Reader);
                var issued = page.Items.Single(n => n.Id == issuedId);
                var expected = culture == "en-US" ? "return by" : culture == "ru-RU" ? "срок возврата" : "қайтару мерзімі";
                f.Check(issued.Message.Contains(expected, StringComparison.OrdinalIgnoreCase) && !issued.Message.Contains("Ескі қазақша хабар", StringComparison.Ordinal), "stored known notifications render in " + culture + " using owned SQL event data");
                f.Check(page.Items.Single(n => n.Id == fineId).Message.Contains("200") && !page.Items.Single(n => n.Id == queuedId).Message.Contains("Ескі қазақша кезек", StringComparison.Ordinal), "fine amounts and reservation notifications localize in " + culture);
                f.Check(page.Items.Single(n => n.Id == unknownId).Title == "Original custom title" && page.Items.Single(n => n.Id == unknownId).Message == "Keep this custom content unchanged", "unknown custom notification remains unchanged in " + culture);
                f.Check(page.Items.Single(n => n.Id == foreignLinkId).Message == "Do not resolve another student's book", "notification joins cannot disclose foreign referenced loan in " + culture);
            }
            await using (var db = f.NewDb())
            {
                f.Check((await db.Notifications.SingleAsync(n => n.Id == issuedId)).Message == "Ескі қазақша хабар", "notification language switching does not rewrite persisted history");
                var foreignRows = new List<NotificationRowViewModel> { new() { Id = issuedId, Type = "Issued", Link = "/Loans/Details/1", Title = "private" } };
                f.Check((await NotificationService.LocalizeAsync(db, DigitalFixture.OtherReader, foreignRows)).Count == 0, "shared notification projection rechecks row ownership before all joins");
            }
        }
        finally { CultureInfo.CurrentCulture = originalCulture; CultureInfo.CurrentUICulture = originalUi; }
    }
}
