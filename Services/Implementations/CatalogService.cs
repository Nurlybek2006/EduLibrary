using EduLibrary.Data;
using EduLibrary.Helpers;
using EduLibrary.Models.Entities;
using EduLibrary.Models.Enums;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace EduLibrary.Services.Implementations;

public sealed class CatalogService(ApplicationDbContext db, ILibraryClock clock, ILogger<CatalogService> logger) : ICatalogService
{
    private const int PageSize = 12;

    public async Task<BookCatalogViewModel> GetBooksAsync(BookFilterViewModel filter, bool canViewArchived, CancellationToken ct = default)
    {
        filter.Search = filter.Search?.Trim();
        filter.Language = filter.Language?.Trim();
        filter.IncludeArchived &= canViewArchived;
        filter.Sort = filter.Sort is "year" or "popularity" or "rating" ? filter.Sort : "title";
        var query = db.Books.AsNoTracking().AsQueryable();
        if (!filter.IncludeArchived) query = query.Where(x => !x.IsArchived);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search;
            var isbnSearch = NormalizeIsbn(search);
            query = query.Where(x => x.Title.Contains(search) || (isbnSearch.Length > 0 && x.ISBN.Replace("-", "").Replace(" ", "").Contains(isbnSearch)) ||
                x.Description.Contains(search) || x.Language.Contains(search) || x.Publisher.Name.Contains(search) ||
                x.BookCategories.Any(c=>c.Category.Name.Contains(search)) || x.BookSubjects.Any(s=>s.Subject.Name.Contains(search)) ||
                x.BookAuthors.Any(a => (a.Author.FirstName + " " + a.Author.LastName).Contains(search) ||
                    (a.Author.LastName + " " + a.Author.FirstName).Contains(search)));
        }
        if (filter.AuthorId.HasValue) query = query.Where(x => x.BookAuthors.Any(a => a.AuthorId == filter.AuthorId));
        if (filter.PublisherId.HasValue) query = query.Where(x => x.PublisherId == filter.PublisherId);
        if (filter.CategoryId.HasValue) query = query.Where(x => x.BookCategories.Any(a => a.CategoryId == filter.CategoryId));
        if (filter.SubjectId.HasValue) query = query.Where(x=>x.BookSubjects.Any(s=>s.SubjectId==filter.SubjectId));
        if (filter.AvailableOnly) query = query.Where(x=>!x.IsArchived && x.Copies.Any(c=>c.Status==CopyStatus.Available));
        if (!string.IsNullOrEmpty(filter.Language)) query = query.Where(x => x.Language == filter.Language);
        var total = await query.CountAsync(ct);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        filter.Page = Math.Clamp(filter.Page, 1, totalPages);
        query = filter.Sort switch
        {
            "year" => query.OrderByDescending(x => x.PublicationYear).ThenBy(x => x.Title).ThenBy(x => x.Id),
            "popularity" => query.OrderByDescending(x => db.Loans.Count(l => l.BookCopy.BookId == x.Id)).ThenBy(x => x.Title).ThenBy(x => x.Id),
            "rating" => query.OrderByDescending(x=>x.Reviews.Where(r=>!r.IsDeleted&&!r.IsHidden).Average(r=>(double?)r.Rating)??0).ThenBy(x=>x.Title).ThenBy(x=>x.Id),
            _ => query.OrderBy(x => x.Title).ThenBy(x => x.Id)
        };
        var books = await query.Skip((filter.Page - 1) * PageSize).Take(PageSize)
            .Select(x=>new BookCardViewModel {
                Id=x.Id,ISBN=x.ISBN,Title=x.Title,Publisher=x.Publisher.Name,Language=x.Language,
                PublicationYear=x.PublicationYear,CoverImageUrl=x.CoverImageUrl,IsArchived=x.IsArchived,
                TotalCopies=x.Copies.Count,AvailableCopies=x.IsArchived?0:x.Copies.Count(c=>c.Status==CopyStatus.Available),
                AverageRating=x.Reviews.Where(r=>!r.IsDeleted&&!r.IsHidden).Average(r=>(double?)r.Rating)??0,
                ReviewCount=x.Reviews.Count(r=>!r.IsDeleted&&!r.IsHidden)
            }).ToListAsync(ct);
        var ids=books.Select(x=>x.Id).ToArray();
        var authors=await db.BookAuthors.AsNoTracking().Where(x=>ids.Contains(x.BookId)).OrderBy(x=>x.Author.LastName)
            .Select(x=>new{x.BookId,Name=x.Author.FirstName+" "+x.Author.LastName}).ToListAsync(ct);
        var categories=await db.BookCategories.AsNoTracking().Where(x=>ids.Contains(x.BookId))
            .Select(x=>new{x.BookId,x.Category.Name}).ToListAsync(ct);
        foreach(var book in books){book.Authors=string.Join(", ",authors.Where(x=>x.BookId==book.Id).Select(x=>x.Name));book.Categories=categories.Where(x=>x.BookId==book.Id).Select(x=>x.Name).Order().ToList();}
        return new BookCatalogViewModel
        {
            Filter = filter, TotalCount = total, TotalPages = totalPages, Books = books,
            Authors = await AuthorOptionsAsync(ct),
            Publishers = await db.Publishers.AsNoTracking().OrderBy(x => x.Name).Select(x => new SelectListItem(x.Name, x.Id.ToString())).ToListAsync(ct),
            Categories = await db.Categories.AsNoTracking().OrderBy(x => x.Name).Select(x => new SelectListItem(x.Name, x.Id.ToString())).ToListAsync(ct),
            Subjects=await db.Subjects.AsNoTracking().OrderBy(x=>x.Name).Select(x=>new SelectListItem(x.Name,x.Id.ToString())).ToListAsync(ct),
            Languages = await db.Books.AsNoTracking().Where(x => !x.IsArchived || filter.IncludeArchived).Select(x => x.Language).Distinct().OrderBy(x => x)
                .Select(x => new SelectListItem(x, x)).ToListAsync(ct)
        };
    }

    public async Task<BookDetailsViewModel?> GetBookAsync(int id, bool canViewArchived, CancellationToken ct = default)
    {
        var book = await db.Books.AsNoTracking().Include(x => x.Publisher)
            .Include(x => x.BookAuthors).ThenInclude(x => x.Author)
            .Include(x => x.BookCategories).ThenInclude(x => x.Category)
              .Include(x=>x.BookSubjects).ThenInclude(x=>x.Subject)
            .Include(x => x.Copies).AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == id && (!x.IsArchived || canViewArchived), ct);
        if (book is null) return null;
        var card=ToCard(book);
        card.AverageRating=await db.Reviews.Where(r=>r.BookId==id&&!r.IsDeleted&&!r.IsHidden).AverageAsync(r=>(double?)r.Rating,ct)??0;
        card.ReviewCount=await db.Reviews.CountAsync(r=>r.BookId==id&&!r.IsDeleted&&!r.IsHidden,ct);
        return new BookDetailsViewModel
        {
            Book = card, Description = book.Description ?? "", PageCount = book.PageCount,
            Subjects=book.BookSubjects.Select(x=>x.Subject.Name).Order().ToList(),
            Categories = book.BookCategories.Select(x => x.Category.Name).Order().ToList(),
            Copies = book.Copies.OrderBy(x => x.InventoryNumber).Select(x => new CopyListItemViewModel
            {
                Id = x.Id, BookId = book.Id, BookTitle = book.Title, InventoryNumber = x.InventoryNumber,
                Barcode = x.Barcode, ShelfLocation = x.ShelfLocation, Condition = x.Condition,
                Status = x.Status, AcquiredAt = x.AcquiredAt, RowVersion = Convert.ToBase64String(x.RowVersion)
            }).ToList()
        };
    }

    public async Task<BookEditViewModel?> GetBookForEditAsync(int id, CancellationToken ct = default)
    {
        var book = await db.Books.AsNoTracking().Include(x => x.BookAuthors).Include(x => x.BookCategories).Include(x=>x.BookSubjects).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (book is null) return null;
        var model = new BookEditViewModel
        {
            Id = book.Id, ISBN = book.ISBN, Title = book.Title, Description = book.Description,
            PublicationYear = book.PublicationYear, Language = book.Language, PageCount = book.PageCount,
            CoverImageUrl = book.CoverImageUrl, PublisherId = book.PublisherId,
            AuthorIds = book.BookAuthors.Select(x => x.AuthorId).ToList(), CategoryIds = book.BookCategories.Select(x => x.CategoryId).ToList()
            ,SubjectIds=book.BookSubjects.Select(x=>x.SubjectId).ToList()
        };
        await PopulateBookOptionsAsync(model, ct);
        return model;
    }

    public async Task PopulateBookOptionsAsync(BookEditViewModel model, CancellationToken ct = default)
    {
        model.Authors = await AuthorOptionsAsync(ct);
        model.Publishers = await db.Publishers.AsNoTracking().OrderBy(x => x.Name).Select(x => new SelectListItem(x.Name, x.Id.ToString())).ToListAsync(ct);
        model.Categories = await db.Categories.AsNoTracking().OrderBy(x => x.Name).Select(x => new SelectListItem(x.Name, x.Id.ToString())).ToListAsync(ct);
        model.Subjects = await db.Subjects.AsNoTracking().OrderBy(x=>x.Name).Select(x=>new SelectListItem(x.Name,x.Id.ToString())).ToListAsync(ct);
    }

    public async Task<int> SaveBookAsync(BookEditViewModel model, CancellationToken ct = default)
    {
        var isbn = NormalizeIsbn(model.ISBN);
        if (await db.Books.AnyAsync(x => x.ISBN.Replace("-", "").Replace(" ", "") == isbn && x.Id != model.Id, ct))
            throw new BusinessException("Бұл ISBN бар кітап тіркелген. Қажет болса, оған жаңа дана қосыңыз.");
        if (!await db.Publishers.AnyAsync(x => x.Id == model.PublisherId, ct))
            throw new BusinessException("Таңдалған баспа табылмады.");
        var authorIds = model.AuthorIds.Distinct().ToList();
        var categoryIds = model.CategoryIds.Distinct().ToList();
        var subjectIds=model.SubjectIds.Distinct().ToList();
        if(await db.Subjects.CountAsync(s=>subjectIds.Contains(s.Id),ct)!=subjectIds.Count)throw new BusinessException("Жарамды пәндерді таңдаңыз.");
        if (authorIds.Count == 0 || await db.Authors.CountAsync(x => authorIds.Contains(x.Id), ct) != authorIds.Count)
            throw new BusinessException("Кемінде бір жарамды авторды таңдаңыз.");
        if (categoryIds.Count == 0 || await db.Categories.CountAsync(x => categoryIds.Contains(x.Id), ct) != categoryIds.Count)
            throw new BusinessException("Кемінде бір жарамды санатты таңдаңыз.");
        if (!string.IsNullOrWhiteSpace(model.CoverImageUrl) &&
            (!Uri.TryCreate(model.CoverImageUrl.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            throw new BusinessException("Мұқаба үшін жарамды HTTPS сілтемесін енгізіңіз.");
        Book book;
        if (model.Id == 0)
        {
            book = new Book { CreatedAt = clock.UtcNow };
            db.Books.Add(book);
        }
        else
        {
            book = await db.Books.Include(x => x.BookAuthors).Include(x => x.BookCategories).Include(x=>x.BookSubjects).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == model.Id, ct)
                ?? throw new BusinessException("Кітап табылмады.");
        }
        book.ISBN = isbn; book.Title = model.Title.Trim(); book.Description = model.Description?.Trim() ?? "";
        book.PublicationYear = model.PublicationYear; book.Language = model.Language.Trim(); book.PageCount = model.PageCount;
        book.CoverImageUrl = string.IsNullOrWhiteSpace(model.CoverImageUrl) ? null : model.CoverImageUrl.Trim();
        book.PublisherId = model.PublisherId; book.UpdatedAt = clock.UtcNow;
        foreach (var item in book.BookAuthors.Where(x => !authorIds.Contains(x.AuthorId)).ToList()) db.BookAuthors.Remove(item);
        foreach (var authorId in authorIds.Where(id => book.BookAuthors.All(x => x.AuthorId != id)))
            book.BookAuthors.Add(new BookAuthor { AuthorId = authorId });
        foreach (var item in book.BookCategories.Where(x => !categoryIds.Contains(x.CategoryId)).ToList()) db.BookCategories.Remove(item);
        foreach (var categoryId in categoryIds.Where(id => book.BookCategories.All(x => x.CategoryId != id)))
            book.BookCategories.Add(new BookCategory { CategoryId = categoryId });
        foreach(var item in book.BookSubjects.Where(x=>!subjectIds.Contains(x.SubjectId)).ToList())db.BookSubjects.Remove(item);
        foreach(var subjectId in subjectIds.Where(id=>book.BookSubjects.All(x=>x.SubjectId!=id)))book.BookSubjects.Add(new(){SubjectId=subjectId});
        await SaveAsync("Кітапты сақтау мүмкін болмады. ISBN бірегейлігін және анықтамалықтарды тексеріңіз.", ct);
        logger.LogInformation("Book {BookId} saved", book.Id);
        return book.Id;
    }

    public async Task SetBookArchivedAsync(int id, bool archived, CancellationToken ct = default)
    {
        var book = await db.Books.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new BusinessException("Кітап табылмады.");
        book.IsArchived = archived; book.UpdatedAt = clock.UtcNow;
        await SaveAsync("Кітап мәртебесін өзгерту мүмкін болмады.", ct);
        logger.LogInformation("Book {BookId} archive status changed to {Archived}", id, archived);
    }

    public async Task<CatalogLookupViewModel> GetAuthorsAsync(string? search, CancellationToken ct = default)
    {
        var query = db.Authors.AsNoTracking();
        search = search?.Trim();
        if (!string.IsNullOrEmpty(search)) query = query.Where(x => (x.FirstName + " " + x.LastName).Contains(search));
        return new CatalogLookupViewModel { Search = search, Items = await query.OrderBy(x => x.LastName).ThenBy(x => x.FirstName)
            .Select(x => new CatalogLookupItemViewModel { Id = x.Id, Name = x.FirstName + " " + x.LastName, Description = x.Biography,
                BookCount = db.BookAuthors.Count(b => b.AuthorId == x.Id) }).ToListAsync(ct) };
    }

    public Task<AuthorEditViewModel?> GetAuthorAsync(int id, CancellationToken ct = default) => db.Authors.AsNoTracking()
        .Where(x => x.Id == id).Select(x => new AuthorEditViewModel { Id = x.Id, FirstName = x.FirstName, LastName = x.LastName, Biography = x.Biography }).SingleOrDefaultAsync(ct);

    public async Task SaveAuthorAsync(AuthorEditViewModel model, CancellationToken ct = default)
    {
        var firstName = model.FirstName.Trim(); var lastName = model.LastName.Trim();
        if (await db.Authors.AnyAsync(x => x.Id != model.Id && x.FirstName == firstName && x.LastName == lastName, ct))
            throw new BusinessException("Бұл автор анықтамалықта бар.");
        var author = model.Id == 0 ? new Author() : await db.Authors.FindAsync([model.Id], ct) ?? throw new BusinessException("Автор табылмады.");
        if (model.Id == 0) db.Authors.Add(author);
        author.FirstName = firstName; author.LastName = lastName; author.Biography = model.Biography?.Trim() ?? "";
        await SaveAsync("Авторды сақтау мүмкін болмады.", ct);
    }

    public async Task DeleteAuthorAsync(int id, CancellationToken ct = default)
    {
        if (await db.BookAuthors.AnyAsync(x => x.AuthorId == id, ct)) throw new BusinessException("Автор кітаптармен байланысқан. Алдымен кітаптағы авторларды өзгертіңіз.");
        var author = await db.Authors.FindAsync([id], ct) ?? throw new BusinessException("Автор табылмады.");
        db.Authors.Remove(author); await SaveAsync("Авторды жою мүмкін болмады: ол басқа жазбамен байланысқан болуы мүмкін.", ct);
    }

    public async Task<CatalogLookupViewModel> GetPublishersAsync(string? search, CancellationToken ct = default)
    {
        var query = db.Publishers.AsNoTracking(); search = search?.Trim();
        if (!string.IsNullOrEmpty(search)) query = query.Where(x => x.Name.Contains(search) || x.Country.Contains(search));
        return new CatalogLookupViewModel { Search = search, Items = await query.OrderBy(x => x.Name)
            .Select(x => new CatalogLookupItemViewModel { Id = x.Id, Name = x.Name, Description = x.Country, BookCount = db.Books.Count(b => b.PublisherId == x.Id) }).ToListAsync(ct) };
    }

    public Task<PublisherEditViewModel?> GetPublisherAsync(int id, CancellationToken ct = default) => db.Publishers.AsNoTracking()
        .Where(x => x.Id == id).Select(x => new PublisherEditViewModel { Id = x.Id, Name = x.Name, Country = x.Country, Website = x.Website }).SingleOrDefaultAsync(ct);

    public async Task SavePublisherAsync(PublisherEditViewModel model, CancellationToken ct = default)
    {
        var name = model.Name.Trim();
        if (await db.Publishers.AnyAsync(x => x.Id != model.Id && x.Name == name, ct)) throw new BusinessException("Бұл атаумен баспа тіркелген.");
        if (!string.IsNullOrWhiteSpace(model.Website) && (!Uri.TryCreate(model.Website.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))) throw new BusinessException("Веб-сайт үшін HTTP немесе HTTPS сілтемесін енгізіңіз.");
        var publisher = model.Id == 0 ? new Publisher() : await db.Publishers.FindAsync([model.Id], ct) ?? throw new BusinessException("Баспа табылмады.");
        if (model.Id == 0) db.Publishers.Add(publisher);
        publisher.Name = name; publisher.Country = model.Country.Trim(); publisher.Website = string.IsNullOrWhiteSpace(model.Website) ? null : model.Website.Trim();
        await SaveAsync("Баспаны сақтау мүмкін болмады.", ct);
    }

    public async Task DeletePublisherAsync(int id, CancellationToken ct = default)
    {
        if (await db.Books.AnyAsync(x => x.PublisherId == id, ct)) throw new BusinessException("Баспа кітаптармен байланысқан. Алдымен кітаптардағы баспаны өзгертіңіз.");
        var publisher = await db.Publishers.FindAsync([id], ct) ?? throw new BusinessException("Баспа табылмады.");
        db.Publishers.Remove(publisher); await SaveAsync("Баспаны жою мүмкін болмады: ол басқа жазбамен байланысқан болуы мүмкін.", ct);
    }

    public async Task<CatalogLookupViewModel> GetCategoriesAsync(string? search, CancellationToken ct = default)
    {
        var query = db.Categories.AsNoTracking(); search = search?.Trim();
        if (!string.IsNullOrEmpty(search)) query = query.Where(x => x.Name.Contains(search));
        return new CatalogLookupViewModel { Search = search, Items = await query.OrderBy(x => x.Name)
            .Select(x => new CatalogLookupItemViewModel { Id = x.Id, Name = x.Name, Description = x.Description,
                BookCount = db.BookCategories.Count(b => b.CategoryId == x.Id) }).ToListAsync(ct) };
    }

    public Task<CategoryEditViewModel?> GetCategoryAsync(int id, CancellationToken ct = default) => db.Categories.AsNoTracking()
        .Where(x => x.Id == id).Select(x => new CategoryEditViewModel { Id = x.Id, Name = x.Name, Description = x.Description }).SingleOrDefaultAsync(ct);

    public async Task SaveCategoryAsync(CategoryEditViewModel model, CancellationToken ct = default)
    {
        var name = model.Name.Trim();
        if (await db.Categories.AnyAsync(x => x.Id != model.Id && x.Name == name, ct)) throw new BusinessException("Бұл атаумен санат тіркелген.");
        var category = model.Id == 0 ? new Category() : await db.Categories.FindAsync([model.Id], ct) ?? throw new BusinessException("Санат табылмады.");
        if (model.Id == 0) db.Categories.Add(category);
        category.Name = name; category.Description = model.Description?.Trim() ?? "";
        await SaveAsync("Санатты сақтау мүмкін болмады.", ct);
    }

    public async Task DeleteCategoryAsync(int id, CancellationToken ct = default)
    {
        if (await db.BookCategories.AnyAsync(x => x.CategoryId == id, ct)) throw new BusinessException("Санат кітаптармен байланысқан. Алдымен кітаптардағы санаттарды өзгертіңіз.");
        var category = await db.Categories.FindAsync([id], ct) ?? throw new BusinessException("Санат табылмады.");
        db.Categories.Remove(category); await SaveAsync("Санатты жою мүмкін болмады: ол басқа жазбамен байланысқан болуы мүмкін.", ct);
    }

    public async Task<CopiesIndexViewModel> GetCopiesAsync(string? search, int? bookId, CopyStatus? status, int page, CancellationToken ct = default)
    {
        var query = db.BookCopies.AsNoTracking(); search = search?.Trim();
        if (!string.IsNullOrEmpty(search)) query = query.Where(x => x.InventoryNumber.Contains(search) || x.Barcode.Contains(search) || x.Book.Title.Contains(search));
        if (bookId.HasValue) query = query.Where(x => x.BookId == bookId);
        if (status.HasValue) query = query.Where(x => x.Status == status);
        var total = await query.CountAsync(ct); var pages = Math.Max(1, (int)Math.Ceiling(total / 20d)); page = Math.Clamp(page, 1, pages);
        var rows = await query.OrderBy(x => x.Book.Title).ThenBy(x => x.InventoryNumber).Skip((page - 1) * 20).Take(20)
            .Select(x => new { Copy = x, Title = x.Book.Title, HasLoans = x.Loans.Any() }).ToListAsync(ct);
        return new CopiesIndexViewModel { Search = search, BookId = bookId, Status = status, Page = page, TotalCount = total, TotalPages = pages,
            Copies = rows.Select(x => new CopyListItemViewModel { Id = x.Copy.Id, BookId = x.Copy.BookId, BookTitle = x.Title,
                InventoryNumber = x.Copy.InventoryNumber, Barcode = x.Copy.Barcode, ShelfLocation = x.Copy.ShelfLocation, Condition = x.Copy.Condition,
                AcquiredAt = x.Copy.AcquiredAt, Status = x.Copy.Status, RowVersion = Convert.ToBase64String(x.Copy.RowVersion), HasLoans = x.HasLoans }).ToList() };
    }

    public async Task<CopyEditViewModel?> GetCopyAsync(int id, CancellationToken ct = default)
    {
        var copy = await db.BookCopies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (copy is null) return null;
        var model = new CopyEditViewModel { Id = copy.Id, BookId = copy.BookId, InventoryNumber = copy.InventoryNumber, Barcode = copy.Barcode,
            Status = copy.Status, ShelfLocation = copy.ShelfLocation, Condition = copy.Condition, AcquiredAt = copy.AcquiredAt,
            RowVersion = Convert.ToBase64String(copy.RowVersion), IsOnLoan = copy.Status == CopyStatus.OnLoan };
        await PopulateCopyOptionsAsync(model, ct); return model;
    }

    public async Task PopulateCopyOptionsAsync(CopyEditViewModel model, CancellationToken ct = default)
    {
        model.Books = await db.Books.AsNoTracking().Where(x => !x.IsArchived || x.Id == model.BookId).OrderBy(x => x.Title)
            .Select(x => new SelectListItem(x.Title + " · " + x.ISBN + (x.IsArchived ? " (мұрағат)" : ""), x.Id.ToString())).ToListAsync(ct);
        if (model.Id != 0) model.IsOnLoan = await db.BookCopies.AnyAsync(x => x.Id == model.Id && x.Status == CopyStatus.OnLoan, ct);
    }

    public async Task<int> SaveCopyAsync(CopyEditViewModel model, CancellationToken ct = default)
    {
        if (model.AcquiredAt.Year < 1900 || model.AcquiredAt.Date > clock.Today)
            throw new BusinessException("Қабылданған күн 1900 жылдан бүгінгі күнге дейін болуы тиіс.");
        var inventoryNumber = model.InventoryNumber.Trim();
        var barcode = string.IsNullOrWhiteSpace(model.Barcode) ? "EL-" + Guid.NewGuid().ToString("N").ToUpperInvariant() : model.Barcode.Trim();
        if (await db.BookCopies.AnyAsync(x => x.Id != model.Id && x.InventoryNumber == inventoryNumber, ct))
            throw new BusinessException("Бұл түгендеу нөмірі басқа данаға тиесілі.");
        if (await db.BookCopies.AnyAsync(x => x.Id != model.Id && x.Barcode == barcode, ct))
            throw new BusinessException("Бұл штрихкод басқа данаға тиесілі.");
        if (!Enum.IsDefined(model.Status)) throw new BusinessException("Жарамды мәртебені таңдаңыз.");
        BookCopy copy;
        if (model.Id == 0)
        {
            if (!await db.Books.AnyAsync(x => x.Id == model.BookId && !x.IsArchived, ct)) throw new BusinessException("Белсенді кітапты таңдаңыз.");
            if (model.Status == CopyStatus.OnLoan) throw new BusinessException("Кітапты беру үшін «Кітап беру» бөлімін пайдаланыңыз.");
            copy = new BookCopy { BookId = model.BookId }; db.BookCopies.Add(copy);
        }
        else
        {
            copy = await db.BookCopies.SingleOrDefaultAsync(x => x.Id == model.Id, ct) ?? throw new BusinessException("Кітап данасы табылмады.");
            if (copy.BookId != model.BookId) throw new BusinessException("Тіркелген дананы басқа кітапқа ауыстыруға болмайды.");
            if (copy.Status == CopyStatus.OnLoan || await db.Loans.AnyAsync(x => x.BookCopyId == copy.Id && x.ReturnedAt == null, ct))
                throw new BusinessException("Оқырмандағы дананы өзгертуге болмайды. Алдымен қайтаруды рәсімдеңіз.");
            if (model.Status == CopyStatus.OnLoan) throw new BusinessException("Кітапты беру үшін «Кітап беру» бөлімін пайдаланыңыз.");
            db.Entry(copy).Property(x => x.RowVersion).OriginalValue = ParseRowVersion(model.RowVersion);
        }
        copy.InventoryNumber = inventoryNumber; copy.Barcode = barcode; copy.Status = model.Status;
        copy.ShelfLocation = model.ShelfLocation.Trim(); copy.Condition = model.Condition.Trim(); copy.AcquiredAt = model.AcquiredAt.Date;
        await SaveAsync("Дананы сақтау мүмкін болмады. Түгендеу нөмірі мен штрихкодтың бірегейлігін тексеріңіз.", ct);
        logger.LogInformation("Copy {CopyId} saved", copy.Id); return copy.Id;
    }

    public async Task DeleteCopyAsync(int id, string rowVersion, CancellationToken ct = default)
    {
        var copy = await db.BookCopies.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new BusinessException("Кітап данасы табылмады.");
        if (copy.Status == CopyStatus.OnLoan || await db.Loans.AnyAsync(x => x.BookCopyId == id, ct))
            throw new BusinessException("Берілу тарихы бар дананы жоюға болмайды. Қажет болса, мәртебесін өзгертіңіз.");
        db.Entry(copy).Property(x => x.RowVersion).OriginalValue = ParseRowVersion(rowVersion);
        db.BookCopies.Remove(copy); await SaveAsync("Дананы жою мүмкін болмады: ол басқа жазбамен байланысқан болуы мүмкін.", ct);
        logger.LogInformation("Copy {CopyId} deleted", id);
    }

    private Task<List<SelectListItem>> AuthorOptionsAsync(CancellationToken ct) => db.Authors.AsNoTracking().OrderBy(x => x.LastName).ThenBy(x => x.FirstName)
        .Select(x => new SelectListItem(x.FirstName + " " + x.LastName, x.Id.ToString())).ToListAsync(ct);

    private static BookCardViewModel ToCard(Book book) => new()
    {
        Id = book.Id, Title = book.Title, ISBN = book.ISBN, Authors = string.Join(", ", book.BookAuthors.OrderBy(x => x.Author.LastName).Select(x => x.Author.FirstName + " " + x.Author.LastName)),
        Publisher = book.Publisher.Name, Language = book.Language, PublicationYear = book.PublicationYear, CoverImageUrl = book.CoverImageUrl,
        TotalCopies = book.Copies.Count, AvailableCopies = book.IsArchived ? 0 : book.Copies.Count(x => x.Status == CopyStatus.Available), IsArchived = book.IsArchived
    };

    private static string NormalizeIsbn(string isbn) => isbn.Replace("-", "").Replace(" ", "").ToUpperInvariant();

    private static byte[] ParseRowVersion(string? value)
    {
        try
        {
            var bytes = Convert.FromBase64String(value ?? "");
            if (bytes.Length != 8) throw new FormatException();
            return bytes;
        }
        catch (FormatException) { throw new BusinessException("Өзгеріс нұсқасы жарамсыз. Бетті жаңартып, қайта көріңіз."); }
    }

    private async Task SaveAsync(string message, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogWarning(ex, "Catalog concurrency conflict");
            throw new BusinessException("Жазбаны басқа қызметкер өзгертті. Бетті жаңартып, қайта көріңіз.");
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Catalog database constraint rejected a change");
            throw new BusinessException(message);
        }
    }
}
