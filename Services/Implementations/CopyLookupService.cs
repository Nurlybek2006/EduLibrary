using EduLibrary.Data;
using EduLibrary.Helpers;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace EduLibrary.Services.Implementations;

public sealed class CopyLookupService(ApplicationDbContext db) : ICopyLookupService
{
    public async Task<CopyDetailsViewModel?> GetDetailsAsync(int id, CancellationToken ct = default)
    {
        var model = await db.BookCopies.AsNoTracking().Where(x => x.Id == id).Select(x => new CopyDetailsViewModel
        {
            Id = x.Id, BookId = x.BookId, BookTitle = x.Book.Title, ISBN = x.Book.ISBN,
            InventoryNumber = x.InventoryNumber, Barcode = x.Barcode, Status = x.Status, ShelfLocation = x.ShelfLocation,
            Condition = x.Condition, AcquiredAt = x.AcquiredAt, BookArchived = x.Book.IsArchived
        }).SingleOrDefaultAsync(ct);
        if (model is null) return null;
        var activeLoan = await db.Loans.AsNoTracking().Where(x => x.BookCopyId == id && x.ReturnedAt == null)
            .Select(x => new { x.Id, x.LoanDate, x.DueDate }).SingleOrDefaultAsync(ct);
        if (activeLoan is not null) { model.ActiveLoanId = activeLoan.Id; model.LoanDate = activeLoan.LoanDate; model.DueDate = activeLoan.DueDate; }
        return model;
    }

    public async Task<int?> FindByCodeAsync(string code, CancellationToken ct = default)
    {
        code = CopyCode.Normalize(code);
        // QR uses Barcode; inventory matching remains available for manual entry.
        var barcodeMatch = await db.BookCopies.AsNoTracking().Where(x => x.Barcode == code).Select(x => (int?)x.Id).SingleOrDefaultAsync(ct);
        return barcodeMatch ?? await db.BookCopies.AsNoTracking().Where(x => x.InventoryNumber == code).Select(x => (int?)x.Id).SingleOrDefaultAsync(ct);
    }

    public async Task<string?> GetQrSvgAsync(int id, CancellationToken ct = default)
    {
        var barcode = await db.BookCopies.AsNoTracking().Where(x => x.Id == id).Select(x => x.Barcode).SingleOrDefaultAsync(ct);
        return barcode is null ? null : CopyCode.CreateQrSvg(barcode);
    }
}
