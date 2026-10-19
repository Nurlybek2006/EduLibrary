using EduLibrary.Helpers;
using EduLibrary.Models.Entities;
using EduLibrary.Models.Enums;
using EduLibrary.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EduLibrary.Data.Seed;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration, bool isDevelopment)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { RoleNames.Admin, RoleNames.Librarian, RoleNames.Student })
            if (!await roles.RoleExistsAsync(role)) Ensure(await roles.CreateAsync(new IdentityRole(role)));

        if (!isDevelopment) return;
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var (key, role, name) in new[] { ("Admin", RoleNames.Admin, "Әкімші"), ("Librarian", RoleNames.Librarian, "Айгерім Кітапханашы"), ("Student", RoleNames.Student, "Аян Сәрсенов") })
        {
            var email = configuration[$"SeedUsers:{key}:Email"];
            var password = configuration[$"SeedUsers:{key}:Password"];
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) continue;
            var user = await users.FindByEmailAsync(email);
            if (user == null)
            {
                user = new ApplicationUser { UserName = email, Email = email, FullName = name, EmailConfirmed = true };
                Ensure(await users.CreateAsync(user, password));
            }
            if (!await users.IsInRoleAsync(user, role)) Ensure(await users.AddToRoleAsync(user, role));
        }

        var db = services.GetRequiredService<ApplicationDbContext>();
        const string seedKey = "academic-foundation-v1";
        if (await db.SeedHistory.AnyAsync(x=>x.Key == seedKey)) return;
        // A single transaction and completion marker make interrupted/repeated seeding safe.
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (await db.Books.AnyAsync() || await db.Students.AnyAsync())
        {
            services.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder")
                .LogInformation("Existing library data detected; demonstration dataset was not applied.");
            return;
        }
        var clock = services.GetRequiredService<ILibraryClock>();
        var today = clock.Today;
        var publishers = new[] { "Prentice Hall", "MIT Press", "O’Reilly Media", "Pearson", "No Starch Press", "Атамұра" }
            .Select((name,i)=>new Publisher { Name=name, Country=i==5 ? "Қазақстан" : "АҚШ", Website=null }).ToArray();
        var categories = new[] { "Бағдарламалау", "Алгоритмдер", "Деректер қоры", "Жасанды интеллект", "Желілер", "Математика", "Физика", "Гуманитарлық ғылымдар", "Дизайн" }
            .Select(name=>new Category { Name=name, Description=$"{name} бойынша оқу әдебиеттері." }).ToArray();
        var authorNames = new[] { ("Robert","Martin"), ("Thomas","Cormen"), ("James","Kurose"), ("Abraham","Silberschatz"), ("Eric","Matthes"), ("Stuart","Russell"), ("Peter","Norvig"), ("Martin","Fowler"), ("Andrew","Tanenbaum"), ("Donald","Knuth"), ("Luciano","Ramalho"), ("Aurélien","Géron"), ("Gilbert","Strang"), ("Stephen","Hawking"), ("Абай","Құнанбайұлы"), ("Мұхтар","Әуезов"), ("Don","Norman"), ("Brian","Kernighan"), ("Dennis","Ritchie"), ("Steve","McConnell") };
        var authors = authorNames.Select(n=>new Author { FirstName=n.Item1, LastName=n.Item2, Biography="Ғылыми және оқу әдебиеттерінің авторы." }).ToArray();
        db.Publishers.AddRange(publishers); db.Categories.AddRange(categories); db.Authors.AddRange(authors);
        var subjects = new[] { "Бағдарламалау негіздері", "Деректер құрылымдары", "Деректер қоры", "Машиналық оқыту", "Жоғары математика", "Компьютерлік желілер" }.Select(n=>new Subject { Name=n }).ToArray();
        db.Subjects.AddRange(subjects);
        // Title, author index, category index, publication year, page count.
        var titles = new (string Title,int Author,int Category,int Year,int Pages)[] {
            ("Clean Code",0,0,2008,464), ("Introduction to Algorithms",1,1,2022,1312),
            ("Computer Networking: A Top-Down Approach",2,4,2021,864), ("Database System Concepts",3,2,2019,1376),
            ("Python Crash Course",4,0,2023,552), ("Artificial Intelligence: A Modern Approach",5,3,2021,1136),
            ("Refactoring",7,0,2018,448), ("Modern Operating Systems",8,0,2023,1184),
            ("The Art of Computer Programming",9,1,2011,672), ("Fluent Python",10,0,2022,1014),
            ("Hands-On Machine Learning",11,3,2022,856), ("Introduction to Linear Algebra",12,5,2023,584),
            ("A Brief History of Time",13,6,1988,256), ("Қара сөздер",14,7,2020,192),
            ("Абай жолы",15,7,2019,608), ("The Design of Everyday Things",16,8,2013,368),
            ("The C Programming Language",17,0,1988,272), ("Code Complete",19,0,2004,960),
            ("The Clean Coder",0,0,2011,256), ("Clean Architecture",0,0,2017,432),
            ("Algorithms Unlocked",1,1,2013,240), ("Computer Networks",8,4,2021,960),
            ("Operating System Concepts",3,0,2018,976), ("Python Flash Cards",4,0,2019,204),
            ("Human Compatible",5,3,2019,352), ("Patterns of Enterprise Application Architecture",7,0,2002,560),
            ("Numerical Linear Algebra",12,5,2018,416), ("The Universe in a Nutshell",13,6,2001,224),
            ("Emotional Design",16,8,2004,272), ("Software Estimation",19,0,2006,352),
            ("Structured Computer Organization",8,0,2013,800), ("Concrete Mathematics",9,5,1994,657),
            ("The Practice of Programming",17,0,1999,288), ("Learning Python",10,0,2013,1648),
            ("Deep Learning with Python",11,3,2021,504), ("Essential Mathematics for Data Science",12,5,2022,256)
        };
        var books = titles.Select((t,i)=>new Book {
            ISBN=$"978-0-00-{i+100000:000000}-0", Title=t.Title, Description=$"{t.Title} — университет студенттеріне арналған оқу және тәжірибелік әдебиет. Кітап негізгі ұғымдар мен практикалық тәсілдерді жүйелі меңгеруге көмектеседі.",
            PublicationYear=t.Year, PageCount=t.Pages, Language=i is 13 or 14 ? "Қазақша" : "English", Publisher=publishers[i%publishers.Length], CreatedAt=clock.UtcNow, UpdatedAt=clock.UtcNow,
            BookAuthors=new List<BookAuthor>{new(){ Author=authors[t.Author] }}, BookCategories=new List<BookCategory>{ new(){Category=categories[t.Category]} },
            Copies=Enumerable.Range(1,1+i%5).Select(j=>new BookCopy { InventoryNumber=$"EL-{i+1:0000}-{j:00}", Barcode=$"EDU{i+1:000000}{j:000}", Status=CopyStatus.Available, ShelfLocation=$"{(char)('A'+t.Category)}-{i%4+1:00}", Condition="Жақсы", AcquiredAt=today.AddMonths(-6) }).ToList()
        }).ToArray();
        books[5].BookAuthors.Add(new BookAuthor { Author=authors[6] });
        books[16].BookAuthors.Add(new BookAuthor { Author=authors[18] });
        db.Books.AddRange(books);
        var studentNames = new[] { "Аян Сәрсенов", "Айдана Әбілова", "Нұрасыл Төлеген", "Әсел Қайратқызы", "Данияр Омаров", "Мадина Ермек", "Әлихан Бекен", "Аружан Серік", "Санжар Мақсат", "Назерке Қанат", "Ерасыл Жұмабек", "Дильназ Асқар", "Мирас Әділ", "Томирис Рахым", "Бекзат Нұрлан", "Айым Сапар", "Ислам Ораз", "Іңкәр Жанат" };
        var demoStudent = string.IsNullOrWhiteSpace(configuration["SeedUsers:Student:Email"]) ? null : await users.FindByEmailAsync(configuration["SeedUsers:Student:Email"]!);
        var students = studentNames.Select((name,i)=>new Student {
            StudentNumber=$"ST2026{i+1:0000}", FullName=name, Email=i==0 && demoStudent!=null ? demoStudent.Email! : $"student{i+1:00}@edulibrary.local",
            PhoneNumber=$"+7701000{i+1:0000}", Faculty=new[]{"Ақпараттық технологиялар","Жаратылыстану ғылымдары","Гуманитарлық ғылымдар"}[i%3], Course=i%4+1, GroupName=$"{new[]{"IT","NS","HU"}[i%3]}-{i%4+1}01", CreatedAt=clock.UtcNow,
            IdentityUserId=i==0 ? demoStudent?.Id : null,
            StudentSubjects=new List<StudentSubject>{ new(){Subject=subjects[i%subjects.Length]}, new(){Subject=subjects[(i+1)%subjects.Length]} },
            Interests=new List<StudentInterest>{new(){Name=categories[i%categories.Length].Name}}
        }).ToArray();
        db.Students.AddRange(students);
        var issuerEmail=configuration["SeedUsers:Librarian:Email"] ?? configuration["SeedUsers:Admin:Email"];
        var issuer=issuerEmail==null ? null : await users.FindByEmailAsync(issuerEmail);
        if (issuer!=null)
        {
            for(var i=0;i<14;i++)
            {
                var copy=books[i].Copies.First(); var returned=i>=9;
                var loanDate=today.AddDays(-25+i); var dueDate=i is >=6 and <=8 ? today.AddDays(i+1) : loanDate.AddDays(14); var returnedAt=dueDate.AddDays(i%3);
                var loan=new Loan { Student=students[i%students.Length],BookCopy=copy,LoanDate=loanDate,DueDate=dueDate,IssuedByUserId=issuer.Id,Notes="Бастапқы оқу деректері", ReturnedAt=returned ? returnedAt : null };
                copy.Status=returned ? CopyStatus.Available : CopyStatus.OnLoan;
                if(returned)
                {
                    loan.Return=new Return { ReturnDate=returnedAt,ReceivedByUserId=issuer.Id,ReturnCondition="Жақсы",Notes="Кітап қабылданды" };
                    var amount=(returnedAt-dueDate).Days*configuration.GetValue<decimal>("Library:FinePerOverdueDay",100m);
                    if(amount>0) loan.Fines.Add(new Fine { Student=students[i%students.Length], Amount=amount,Reason="Қайтару мерзімі кешіктірілген",CreatedAt=clock.UtcNow,Status=FineStatus.Unpaid });
                }
                db.Loans.Add(loan);
            }
        }
        db.SeedHistory.Add(new SeedHistory { Key=seedKey,AppliedAt=clock.UtcNow });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ",result.Errors.Select(e=>e.Description)));
    }
}
