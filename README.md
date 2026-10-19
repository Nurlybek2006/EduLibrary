# EduLibrary

EduLibrary — университеттің оқу әдебиеттері кітапханасын басқаратын ASP.NET Core MVC қосымшасы. Кітап сипаттамасы мен физикалық дана бөлек сақталады; каталог, оқырмандар, кітап беру, қайтару және айыппұлдар SQL Server арқылы жұмыс істейді. Интерфейс қазақ тілінде, ашық және қараңғы тақырыптарды қолдайды.

## Технологиялар

- .NET 10 LTS, ASP.NET Core MVC, Razor Views, C#.
- Entity Framework Core 10.0.12, SQL Server, Code First migrations.
- ASP.NET Core Identity: `Admin`, `Librarian`, `Student` рөлдері.
- Bootstrap 5, арнайы CSS/JavaScript, Lucide белгішелері.
- MVC → Service Layer → EF Core; сервистер Dependency Injection арқылы беріледі.

Бұл — жеке EduLibrary жобасы. `IntelligentProgrammingPlatform` жобасының файлдары өзгертілмейді.

## Жылдам іске қосу

Қажет: .NET 10 SDK, SQL Server және осы Windows пайдаланушысына деректер базасын жасау/пайдалану құқығы. SSMS 21 — базаны қарауға арналған құрал; SQL Server қозғалтқышы бөлек орнатылуы тиіс.

Жоба түбірінде PowerShell арқылы:

```powershell
dotnet --info
dotnet restore
dotnet tool restore --tool-manifest .\dotnet-tools.json
dotnet build
dotnet run --launch-profile http
```

Браузерде **http://localhost:5298** ашыңыз. HTTPS профилі: `dotnet run --launch-profile https`, мекенжайы **https://localhost:7296**. Жергілікті HTTPS сертификаты қажет болса: `dotnet dev-certs https --trust`.

`http` және `https` профильдері `Development` ортасын қосады. Осы ортада бірінші іске қосылғанда migrations автоматты қолданылады және бос база оқу деректерімен толтырылады. SQL Server қосылмаса, бағдарлама қатені журналға жазып тоқтайды; SQL орнына жалған деректерге ауыспайды.

## SQL Server қосылымы

Осы жұмыс орнында анықталған SQL Server default instance — `.`; қозғалтқыштың негізгі нұсқасы — 16. Деректер базасының аты — **EduLibraryDb**. Development конфигурациясы `appsettings.Development.json` ішінде; басқа компьютерде өз instance атауыңызды орнатыңыз.

| Орнатылым | Connection string мысалы |
|---|---|
| Default instance | `Server=localhost;Database=EduLibraryDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=False` |
| SQL Express | `Server=.\SQLEXPRESS;Database=EduLibraryDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=False` |
| LocalDB | `Server=(localdb)\MSSQLLocalDB;Database=EduLibraryDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=False` |

Development қосылымын user-secrets арқылы беру:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=EduLibraryDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=False"
```

`appsettings.Local.json` файлы тек Development ортасында оқылады және Git арқылы жіберілмейді. Қолданба оны user-secrets-тен кейін жүктейді: бірдей кілт local файлда болса, сол мән басым болады. User-secrets-ке көшірген кілттерді local файлдан алып тастаңыз. Environment variables және командалық жол аргументтері local файлдан басым.

Environment variable баламасы:

```powershell
$env:ConnectionStrings__DefaultConnection = "Server=localhost;Database=EduLibraryDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=False"
```

`TrustServerCertificate=True` мысалдары жергілікті әзірлеуге арналған. Production қосылымында сенімді SQL сертификатын, шектеулі құқықтары бар аккаунтты және сыртқы құпиялар сақтау орнын пайдаланыңыз; connection string пен парольдерді репозиторийге қоспаңыз.

### SSMS 21 арқылы қарау

1. SSMS 21 ашып, **Database Engine** түрін таңдаңыз.
2. Server name: `.` немесе өз SQL instance атауыңыз; Authentication: **Windows Authentication**.
3. Қосылғаннан кейін **Databases → Refresh → EduLibraryDb → Tables** ашыңыз.
4. `Books`, `BookCopies`, `Students`, `Loans`, `Returns`, `Fines`, `FinePayments` және `AspNetUsers` кестелерін қараңыз. `__EFMigrationsHistory` қолданылған migrations тізімін сақтайды.

Қосылым қатесінде SQL Server қызметінің іске қосылғанын, instance атауын және қолданбаны іске қосқан Windows аккаунтының SQL рұқсаттарын тексеріңіз. Базаны қолмен жасаудың қажеті жоқ: рұқсат болғанда EF migration оны жасайды.

## Migrations және бастапқы деректер

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet tool restore --tool-manifest .\dotnet-tools.json
dotnet ef migrations list
dotnet ef database update
```

Модельді кейін өзгерткенде жаңа migration жасаңыз; қолданыстағы migration тарихын өшірмеңіз:

```powershell
dotnet ef migrations add DescribeYourChange --output-dir Data/Migrations
dotnet ef database update
```

Веб-серверді ашпай, Development базасын дайындау:

```powershell
dotnet run --launch-profile http -- --initialize-db
```

`Database:ApplyMigrations` Development ортасында `true`, негізгі конфигурацияда `false`. Production ортасында migrations-ті басқарылатын орналастыру қадамы ретінде қолданыңыз; connection string-ті міндетті түрде сырттан беріңіз. `DbSeeder` барлық ортада рөлдерді тексереді, ал демо пайдаланушылар мен оқу деректерін тек Development ортасында жасайды.

Толық бастапқы жинақ: **36 кітап, 106 физикалық дана, 20 автор, 6 баспа, 9 санат, 18 студент, 6 пән**, студент қызығушылықтары. Демо қызметкер конфигурациясы берілсе, белсенді және қайтарылған **14 кітап беру жазбасы**, мерзімі өткен кітаптар мен айыппұлдар қосылады. Бұл сандар — бастапқы оқу жинағы; экрандар статистиканы SQL Server-ден есептейді.

Seed транзакциямен орындалып, `SeedHistory` белгісімен қайталанудан қорғалады. Бұрыннан кітаптары немесе студенттері бар базаға демо каталог араластырылмайды. Толық демо сценарий үшін `SeedUsers` мәндерін алғашқы толтыруға дейін беріңіз. Seed-ті қайта іске қосу бұрынғы аккаунттың паролін өзгертпейді.

## Демо аккаунттар

Осы жұмыс орнында Development үшін жеке кездейсоқ парольдері бар `appsettings.Local.json` жасалған. Парольдерді тек өз компьютеріңізде қараңыз:

```powershell
Get-Content .\appsettings.Local.json
```

| Рөл | Development поштасы |
|---|---|
| Әкімші | `admin@edulibrary.local` |
| Кітапханашы | `librarian@edulibrary.local` |
| Студент | `student@edulibrary.local` |

Жаңа компьютерде аккаунттарды user-secrets арқылы конфигурациялауға болады. Төмендегі пароль орындарына өзіңіз жасаған әртүрлі құпиясөздерді жазыңыз:

```powershell
dotnet user-secrets set "SeedUsers:Admin:Email" "admin@edulibrary.local"
dotnet user-secrets set "SeedUsers:Admin:Password" "<әкімшінің жеке құпиясөзі>"
dotnet user-secrets set "SeedUsers:Librarian:Email" "librarian@edulibrary.local"
dotnet user-secrets set "SeedUsers:Librarian:Password" "<кітапханашының жеке құпиясөзі>"
dotnet user-secrets set "SeedUsers:Student:Email" "student@edulibrary.local"
dotnet user-secrets set "SeedUsers:Student:Password" "<студенттің жеке құпиясөзі>"
```

Құпиясөз кемінде 10 таңбадан тұрып, бас әріп, кіші әріп, сан және арнайы таңба қамтуы керек. Кодта production үшін бастапқы пароль жоқ. User-secrets әзірлеуге арналған; оны production құпиялар сақтау орны ретінде пайдаланбаңыз.

## Модульдер және рұқсаттар

| Модуль | Негізгі мүмкіндіктер |
|---|---|
| Dashboard | SQL статистикасы, соңғы беру/қайтару жазбалары, кешігу ескертулері, танымал кітаптар |
| Каталог | Іздеу; автор, баспа, санат, тіл сүзгілері; сұрыптау; беттеу; кітап мәліметтері |
| Кітаптар мен анықтамалықтар | Кітап, автор, баспа, санат CRUD; кітапты архивтеу |
| Физикалық даналар | Инвентарлық нөмір, бірегей штрихкод, сөре, жағдай, мәртебе |
| Студенттер | Іздеу/сүзгілеу, профиль, пәндер, қызығушылықтар, кітап алу және айыппұл тарихы, белсенділікті тоқтату |
| Кітап беру / қайтару | Дана таңдау, мерзім белгілеу, транзакция, кешігу есебі, жағдайын тіркеу |
| Айыппұлдар | Нақты және болжамды сома, толық/ішінара төлем, қалдық және төлем тарихы |
| Аккаунттар | Кіру, студенттің тіркелуі, шығу, жеке профиль, кітапханашы аккаунтын басқару |

**Admin** барлық басқару модульдерін және кітапханашы аккаунттарын басқарады. **Librarian** каталогты, оқырмандарды және кітап айналымын басқарады. **Student** каталогты, өзінің тарихы мен айыппұлдарын көреді және жеке байланыс деректерін, пәндерін, қызығушылықтарын өңдейді. Академиялық деректерді студенттің профиль формасы өзгерте алмайды.

Жаңа тіркелу тек `Student` рөлін береді. Бұрыннан тіркелген студент нөмірі арқылы басқа оқырманның жазбасын иемденуге болмайды. Белсенділігі тоқтатылған студентке жаңа кітап берілмейді; байланыстырылған аккаунт бұғатталады, тарих сақталады. Әкімші аккаунтын немесе өз аккаунтыңызды кітапханашылар тізімінен тоқтату мүмкін емес.

Басты маршруттар: `/Books`, `/Dashboard`, `/Students`, `/Loans`, `/Returns`, `/Fines`, `/Staff`, `/Account/Login`, `/Account/Register`, `/Account/Profile`. Студент өзінің тарихын `/Students/MyHistory` арқылы ашады.

## Кітап беру, қайтару және айыппұл

1. Қызметкер белсенді студент пен `Available` дананы таңдап, қайтару мерзімін береді.
2. `Loan` жазбасы мен дананың `OnLoan` мәртебесі бір транзакцияда сақталады.
3. Қайтарғанда бір ғана `Return` жасалып, дана жағдайына сай `Available` немесе `Damaged` болады.
4. Кешігу күндері `max(0, қайтарылған күн − DueDate)` формуласымен анықталады. Қайтарылмаған кітап үшін ағымдағы күн қолданылады және сома болжам ретінде көрсетіледі.
5. Қайтарғанда айыппұл сомасы бекітіледі. Қызметкер төлемді тіркегенде қалдық пен мәртебе жаңарады; бұл сыртқы банк төлем шлюзі емес.

Мысал: мерзімі 10 қазан, қайтару 15 қазан → 5 күн × 100 ₸ = **500 ₸**. Күндер кітапхананың **Asia/Qyzylorda** уақыт белдеуімен есептеледі; техникалық жасау/төлем уақыттары UTC-де сақталады.

Саясат `appsettings.json` арқылы конфигурацияланады:

```json
"Library": {
  "FinePerOverdueDay": 100,
  "DefaultLoanDays": 14
}
```

Қатар кітап беру `RowVersion`, транзакция және белсенді `Loan` үшін бірегей индекс арқылы қорғалған. Бір дананы екі рет қатар беру немесе бір қарызды екі рет қайтару қабылданбайды. Тарихы бар деректерді кездейсоқ жоюға FK шектеулері кедергі жасайды.

## Тексеру

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet restore
dotnet build
dotnet ef migrations list
dotnet run --project tests/EduLibrary.IntegrationChecks
```

Нақты ортада `dotnet restore` және `dotnet build` сәтті аяқталды: 0 қате, 0 ескерту. `EduLibraryDb` миграциясы қолданылды, seed қайталанғанда деректер көбеймейтіні тексерілді. **149 HTTP тексеру өтті**: негізгі маршруттар, үш рөлдің қолжетімділігі, жеке деректерді оқшаулау, CSRF және нүкте/үтірмен берілген төлем сомаларын валидациялау. Есеп: `tests/http_smoke.results.json`.

SQL Server-де **52 интеграциялық тексеру сәтті өтті**: кітап беру/қайтару, кешігу және айыппұл, ішінара/толық төлем, қатар беру, ескірген concurrency нұсқасы, студент деректерінің оқшаулануы және Dashboard көрсеткіштері. Harness `EduLibraryChecks_<guid>` атты жеке уақытша база жасап, соңында тек өзі жасаған базаны жояды; бұл тазарту тексерілген. Негізгі `EduLibraryDb` қолданылмайды.

Әдепкі сервер — `.` және Windows Authentication. Басқа серверді таңдау:

```powershell
$env:EDULIBRARY_TEST_CONNECTION_STRING = "Server=localhost;Trusted_Connection=True;TrustServerCertificate=True"
dotnet run --project tests/EduLibrary.IntegrationChecks
```

Тексеру аккаунтына уақытша база жасау/жою құқығы керек. Harness кестелерді жобаның `MigrateAsync` миграциялары арқылы жасайды және модельдің соңғы миграцияға сәйкестігін тексереді. Нақты қайталау нәтижесін команданың шығысынан қараңыз; build нәтижесі SQL қосылымы мен барлық қолданушы сценарийлері тексерілгенін өздігінен білдірмейді.

HTTP тексерулерін қайталау: қосымша жұмыс істеп тұрғанда `python tests/http_smoke.py`. Бұл тексеру кіру/шығу және қабылданбайтын сұрауларды ғана орындайды, негізгі деректерді өзгертпейді. Браузер құралы бұл сессияда қолжетімді болмағандықтан, экрандарды визуалды тексеру орындалмады.

Қолмен тексеру: үш рөлмен кіру; каталогта іздеу; кітап пен дана қосу; студент таңдап кітап беру; оны кешіктіріп қайтару; айыппұлға ішінара және толық төлем тіркеу; студенттің тек өз тарихын көретінін тексеру. Нақты SQL қолжетімділігі, migration/seed және сценарий нәтижелері орындалған ортаға тәуелді.

## Жоба құрылымы

```text
Controllers/                HTTP сұраулар мен жауаптар
Data/ApplicationDbContext.cs Identity және кітапхана DbContext
Data/Configurations/        Fluent API, индекстер, байланыстар, шектеулер
Data/Migrations/            Деректер базасының нұсқалар тарихы
Data/Seed/                  Рөлдер, Development аккаунттары және оқу жинағы
Models/Entities/            Деректер моделі
Models/Enums/               Дана және айыппұл мәртебелері
ViewModels/                 Формалар мен экрандарға арналған модельдер
Services/Interfaces/        Сервис келісімдері
Services/Implementations/   Бизнес ережелері және EF сұраулары
Helpers/                    Конфигурация, бизнес қатесі, Identity хабарламалары
Views/                      Қазақша Razor беттері
wwwroot/                    CSS, JavaScript, жергілікті frontend кітапханалары
tests/                      SQL Server интеграциялық тексерулері
```

Құрылым мен негізгі сценарийлердің түсіндірмесі: [STUDY.md](STUDY.md).

## Келесі кезең

AI Recommendations, кеңейтілген Reports/Analytics, Reviews, Notifications және дизайнның соңғы толықтырулары келесі кезеңге қалдырылған. Тиісті интерфейс бөлімдері жоспарланған мүмкіндік ретінде белгіленген; олар дайын AI немесе есеп генераторы ретінде көрсетілмейді. Production орналастыруы үшін бөлек орта құпияларын, HTTPS/SQL сертификаттарын, резервтік көшіруді және ұйымның рұқсат саясатын дайындау қажет.


