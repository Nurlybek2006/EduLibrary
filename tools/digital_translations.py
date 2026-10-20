"""Produce the merge input consumed by the root shared-resource generator."""
from pathlib import Path
import json,re
rows = '''
A digital edition belongs to an existing book. It does not create or change physical copies.|Электрондық нұсқа каталогтағы кітапқа тиесілі. Ол физикалық даналарды құрмайды және өзгертпейді.|Электронное издание связано с книгой каталога. Оно не создаёт и не изменяет физические экземпляры.
A library without distance|Қашықтыққа бағынбайтын кітапхана|Библиотека без расстояний
Access|Қолжетімділік|Доступ
Access and publication|Қолжетімділік және жариялау|Доступ и публикация
Access level|Қолжетімділік деңгейі|Уровень доступа
Action|Әрекет|Действие
Add an authorized PDF or EPUB to the library.|Кітапханаға таратуға рұқсат етілген PDF немесе EPUB қосыңыз.|Добавьте в библиотеку PDF или EPUB с подтверждёнными правами.
Add bookmark|Бетбелгі қосу|Добавить закладку
All authors|Барлық авторлар|Все авторы
All categories|Барлық санаттар|Все категории
All formats|Барлық форматтар|Все форматы
All languages|Барлық тілдер|Все языки
All recommendations|Барлық ұсыныстар|Все рекомендации
All subjects|Барлық пәндер|Все дисциплины
Allow a separate file download|Файлды бөлек жүктеп алуға рұқсат ету|Разрешить отдельное скачивание файла
Apply filters|Сүзгілерді қолдану|Применить фильтры
Archive digital edition|Электрондық нұсқаны архивтеу|Архивировать электронное издание
Archive this digital edition? It will no longer be available to readers.|Осы электрондық нұсқаны архивтейсіз бе? Оқырмандар оған қол жеткізе алмайды.|Архивировать это электронное издание? Оно станет недоступно читателям.
Archived|Архивтелген|В архиве
Author|Автор|Автор
Back to book information|Кітап туралы ақпаратқа оралу|К сведениям о книге
Back to digital library|Электрондық кітапханаға оралу|В электронную библиотеку
Back to management|Басқаруға оралу|К управлению
Book|Кітап|Книга
Book content|Кітап мазмұны|Содержимое книги
Bookmark removed|Бетбелгі жойылды|Закладка удалена
Bookmark saved|Бетбелгі сақталды|Закладка сохранена
Bookmark title|Бетбелгі атауы|Название закладки
Bookmarks|Бетбелгілер|Закладки
Browse by subject|Пән бойынша қарау|Поиск по дисциплинам
Browse digital books|Электрондық кітаптарды қарау|Смотреть электронные книги
Cancel|Бас тарту|Отмена
Catalog book|Каталогтағы кітап|Книга каталога
Catalog information|Каталогтағы ақпарат|Сведения в каталоге
Category|Санат|Категория
Chapter|Тарау|Глава
Choose a book|Кітапты таңдаңыз|Выберите книгу
Close|Жабу|Закрыть
Close bookmarks|Бетбелгілерді жабу|Закрыть закладки
Completed|Оқылды|Прочитано
Completed books|Оқылған кітаптар|Прочитанные книги
Content language|Мазмұн тілі|Язык содержимого
Continue from your saved place|Сақталған орыннан жалғастырыңыз|Продолжите с сохранённого места
Continue reading|Оқуды жалғастыру|Продолжить чтение
Current file|Ағымдағы файл|Текущий файл
Current page|Ағымдағы бет|Текущая страница
Currently reading|Қазір оқып жүрмін|Сейчас читаю
Dark|Қараңғы|Тёмная
Digital collection|Электрондық қор|Электронный фонд
Digital edition|Электрондық нұсқа|Электронное издание
Digital editions|Электрондық нұсқалар|Электронные издания
Digital library|Электрондық кітапхана|Электронная библиотека
Digital library navigation|Электрондық кітапхана навигациясы|Навигация электронной библиотеки
Digital reading does not change physical copy availability or borrowing records.|Электрондық оқу физикалық даналардың қолжетімділігін немесе кітап беру жазбаларын өзгертпейді.|Электронное чтение не меняет доступность физических экземпляров и записи о выдаче.
Discover|Кітаптарды ашу|Обзор
Discover books|Кітап таңдау|Выбрать книгу
Discover digital books|Электрондық кітаптарды ашыңыз|Откройте электронные книги
Download|Жүктеп алу|Скачать
Draft|Жоба|Черновик
Drafts remain visible only to library staff. Publishing requires confirmed rights and complete provenance.|Жобаларды тек кітапхана қызметкерлері көреді. Жариялау үшін құқықтар расталып, шығу тегі толық көрсетілуі керек.|Черновики видны только сотрудникам библиотеки. Для публикации нужны подтверждённые права и полные сведения о происхождении.
Edit|Өңдеу|Редактировать
Edit digital edition|Электрондық нұсқаны өңдеу|Редактировать электронное издание
Edition|Нұсқа|Издание
File and catalog book|Файл және каталогтағы кітап|Файл и книга каталога
Find next|Келесісін табу|Найти далее
Fit width|Еніне сыйдыру|По ширине
Font size|Қаріп өлшемі|Размер шрифта
Format|Формат|Формат
Fullscreen|Толық экран|Полный экран
Fullscreen is unavailable in this browser.|Бұл браузерде толық экран қолжетімсіз.|В этом браузере полноэкранный режим недоступен.
I confirm that the recorded rights permit this digital edition to be distributed.|Көрсетілген құқықтар осы электрондық нұсқаны таратуға рұқсат беретінін растаймын.|Подтверждаю, что указанные права разрешают распространение этого электронного издания.
Language|Тіл|Язык
Last read|Соңғы оқылған уақыт|Последнее чтение
Library|Кітапхана|Библиотека
Library collection|Кітапхана қоры|Библиотечный фонд
License|Лицензия|Лицензия
License URL|Лицензия сілтемесі|Ссылка на лицензию
License and provenance|Лицензия және шығу тегі|Лицензия и происхождение
License name|Лицензия атауы|Название лицензии
License type|Лицензия түрі|Тип лицензии
Light|Ашық|Светлая
Line spacing|Жол аралығы|Межстрочный интервал
Loading your book…|Кітап жүктелуде…|Книга загружается…
Manage digital content|Электрондық қорды басқару|Управление электронным фондом
Match {0} of {1}|{1} сәйкестіктің {0}-і|Совпадение {0} из {1}
Maximum file size: {0} MB|Файлдың ең үлкен көлемі: {0} МБ|Максимальный размер файла: {0} МБ
Most read|Ең көп оқылған|Самые читаемые
My digital books|Менің электрондық кітаптарым|Мои электронные книги
Next|Келесі|Далее
Next page|Келесі бет|Следующая страница
No authorized digital edition is available for this book.|Бұл кітаптың таратуға рұқсат етілген электрондық нұсқасы жоқ.|Для этой книги нет электронного издания с подтверждёнными правами.
No bookmarks yet. Save a page or passage to return to it later.|Әзірге бетбелгі жоқ. Кейін оралу үшін бетті немесе үзіндіні сақтаңыз.|Закладок пока нет. Сохраните страницу или фрагмент, чтобы вернуться к нему позже.
No digital editions found|Электрондық нұсқалар табылмады|Электронные издания не найдены
No matching text found|Сәйкес мәтін табылмады|Совпадений не найдено
Note (optional)|Ескертпе (міндетті емес)|Заметка (необязательно)
Only materials with confirmed distribution rights are published. Digital reading does not borrow a physical copy.|Тек тарату құқығы расталған материалдар жарияланады. Электрондық оқу физикалық дананы алмайды.|Публикуются только материалы с подтверждёнными правами. Электронное чтение не занимает физический экземпляр.
Open|Ашу|Открыть
Open a digital book to begin. Your progress will be saved to your account.|Бастау үшін электрондық кітапты ашыңыз. Оқу прогресі есептік жазбаңызға сақталады.|Откройте электронную книгу. Прогресс чтения будет сохранён в вашей учётной записи.
PDF or EPUB file|PDF немесе EPUB файлы|Файл PDF или EPUB
PDF page|PDF беті|Страница PDF
Page {0}|{0}-бет|Страница {0}
Page {0} of {1}|{1} беттің {0}-і|Страница {0} из {1}
Pages|Беттер|Страницы
Pagination|Беттерге өту|Переход по страницам
Previous|Алдыңғы|Назад
Previous page|Алдыңғы бет|Предыдущая страница
Progress|Прогресс|Прогресс
Provenance|Шығу тегі|Происхождение
Publish only files with verified distribution rights. Commercial catalog metadata does not grant digital distribution permission.|Тек тарату құқығы тексерілген файлдарды жариялаңыз. Коммерциялық кітаптың каталогтағы ақпараты оны электронды таратуға рұқсат бермейді.|Публикуйте только файлы с подтверждёнными правами. Наличие коммерческой книги в каталоге не даёт права на её электронное распространение.
Publish this digital edition|Осы электрондық нұсқаны жариялау|Опубликовать это электронное издание
Published|Жарияланған|Опубликовано
Read licensed PDF and EPUB books in your browser. Your place and bookmarks travel with you.|Рұқсат етілген PDF және EPUB кітаптарын браузерде оқыңыз. Оқу орныңыз бен бетбелгілеріңіз әр құрылғыда сақталады.|Читайте разрешённые PDF и EPUB в браузере. Место чтения и закладки доступны на разных устройствах.
Read online|Онлайн оқу|Читать онлайн
Readers|Оқырмандар|Читатели
Reading bookmark|Оқу бетбелгісі|Закладка для чтения
Reading controls|Оқуды басқару|Управление чтением
Reading progress|Оқу прогресі|Прогресс чтения
Reading theme|Оқу тақырыбы|Тема чтения
Ready to read|Оқуға дайын|Готово к чтению
Recently added|Жақында қосылған|Недавно добавленные
Recently opened|Жақында ашылған|Недавно открытые
Recommended for you|Сізге арналған ұсыныстар|Рекомендуем вам
Reflowable text|Экранға бейімделетін мәтін|Адаптивный текст
Remove|Жою|Удалить
Remove this bookmark?|Осы бетбелгіні жоясыз ба?|Удалить эту закладку?
Replace file (optional)|Файлды ауыстыру (міндетті емес)|Заменить файл (необязательно)
Reset|Тазарту|Сбросить
Retry saving|Сақтауды қайталау|Повторить сохранение
Rights|Құқықтар|Права
Rights holder|Құқық иесі|Правообладатель
Save a bookmark|Бетбелгіні сақтау|Сохранение закладки
Save bookmark|Бетбелгіні сақтау|Сохранить закладку
Save digital edition|Электрондық нұсқаны сақтау|Сохранить электронное издание
Saving your place…|Оқу орны сақталуда…|Сохраняем место чтения…
Search|Іздеу|Поиск
Search by title or author|Атауы немесе авторы бойынша іздеу|Поиск по названию или автору
Search digital books|Электрондық кітап іздеу|Поиск электронных книг
Search in book|Кітаптан іздеу|Поиск в книге
Searching the book…|Кітаптан ізделуде…|Поиск по книге…
Sepia|Сепия|Сепия
Sign in to read|Оқу үшін кіріңіз|Войдите для чтения
Sort by|Сұрыптау|Сортировка
Source or provenance URL|Дереккөз немесе шығу тегі сілтемесі|Ссылка на источник или происхождение
Status|Мәртебе|Статус
Subject|Пән|Дисциплина
Subjects and filters|Пәндер және сүзгілер|Дисциплины и фильтры
Table of contents|Мазмұны|Оглавление
The book could not be opened. Check your connection and try again.|Кітапты ашу мүмкін болмады. Байланысты тексеріп, қайта көріңіз.|Не удалось открыть книгу. Проверьте соединение и повторите попытку.
The bookmark could not be saved. Please try again.|Бетбелгі сақталмады. Қайта көріңіз.|Не удалось сохранить закладку. Повторите попытку.
The server checks the actual file structure and removes active EPUB content.|Сервер файлдың нақты құрылымын тексеріп, EPUB ішіндегі белсенді мазмұнды жояды.|Сервер проверяет структуру файла и удаляет активное содержимое EPUB.
This reader requires JavaScript to render pages and save your reading position.|Беттерді көрсету және оқу орнын сақтау үшін JavaScript қажет.|Для отображения страниц и сохранения места чтения требуется JavaScript.
Title|Атауы|Название
Try another format, subject or search phrase. Only registered digital files appear here.|Басқа форматты, пәнді немесе іздеу сөзін таңдаңыз. Мұнда тек нақты тіркелген файлдар көрсетіледі.|Попробуйте другой формат, дисциплину или запрос. Здесь отображаются только зарегистрированные файлы.
Update file metadata, rights and access. Leave the file field empty to keep the current edition.|Файл ақпараты, құқықтары мен қолжетімділігін жаңартыңыз. Ағымдағы файлды сақтау үшін файл өрісін бос қалдырыңыз.|Обновите сведения, права и доступ. Оставьте поле файла пустым, чтобы сохранить текущую версию.
Upload a PDF or EPUB and record its license before publishing.|PDF немесе EPUB жүктеп, жарияламас бұрын лицензиясын көрсетіңіз.|Загрузите PDF или EPUB и укажите лицензию перед публикацией.
Upload digital edition|Электрондық нұсқа жүктеу|Загрузить электронное издание
Upload, license and publish digital editions linked to existing catalog books.|Каталогтағы кітаптарға электрондық нұсқаларды жүктеп, лицензиясын көрсетіп, жариялаңыз.|Загружайте, лицензируйте и публикуйте электронные издания для книг каталога.
View license|Лицензияны қарау|Посмотреть лицензию
View my shelf|Менің сөреме өту|Перейти к моей полке
View original source|Бастапқы дереккөзді қарау|Посмотреть первоисточник
Your next chapter starts here|Келесі тарауыңыз осы жерден басталады|Ваша следующая глава начинается здесь
Your place could not be saved. Keep this page open and try again.|Оқу орны сақталмады. Бетті жаппай, қайта сақтап көріңіз.|Не удалось сохранить место чтения. Не закрывайте страницу и повторите попытку.
Your place is saved|Оқу орны сақталды|Место чтения сохранено
Your reading history, saved places and bookmarks, securely linked to your account.|Оқу тарихыңыз, сақталған орындарыңыз бен бетбелгілеріңіз есептік жазбаңызға қауіпсіз байланыстырылған.|История чтения, сохранённые места и закладки безопасно связаны с вашей учётной записью.
Your reading, everywhere|Оқуыңыз әрқашан өзіңізбен бірге|Ваше чтение всегда с вами
Zoom in|Үлкейту|Увеличить
Zoom out|Кішірейту|Уменьшить
{0} editions|{0} нұсқа|Изданий: {0}
{0} pages|{0} бет|Страниц: {0}
{0}% read|{0}% оқылды|Прочитано {0}%
Unknown|Анықталмаған|Не определено
Original|Түпнұсқа материал|Оригинальный материал
PublicDomain|Қоғамдық игілік|Общественное достояние
CreativeCommons|Creative Commons лицензиясы|Лицензия Creative Commons
AuthorPermission|Автор рұқсаты|Разрешение автора
Institutional|Университет лицензиясы|Университетская лицензия
Authenticated|Жүйеге кірген оқырмандар|Авторизованные читатели
Students|Студенттер|Студенты
Staff|Кітапхана қызметкерлері|Сотрудники библиотеки
'''.strip().splitlines()
english_names={'PublicDomain':'Public domain','CreativeCommons':'Creative Commons','AuthorPermission':'Author permission','Institutional':'Institutional license','Authenticated':'Signed-in readers','Staff':'Library staff','Original':'Original material','Unknown':'Unspecified'}
entries=[]
for line in rows:
 key,kk,ru=line.split('|',2)
 entries.append(dict(key=key,kk=kk,ru=ru,en=english_names.get(key,key)))
root=Path(__file__).resolve().parents[1]
paths=list((root/'Views/DigitalLibrary').glob('*.cshtml'))+list((root/'Views/Reader').glob('*.cshtml'))+list((root/'Views/Shared/Components/BookDigitalAvailability').glob('*.cshtml'))
required={key for path in paths for key in re.findall(r'T\["([^"\n]+)"',path.read_text(encoding='utf8'))}
missing=required-{entry['key'] for entry in entries}
if missing: raise ValueError('Missing digital translations: '+repr(sorted(missing)))
target=root/'Resources/digital-translations.json'
target.parent.mkdir(parents=True,exist_ok=True)
target.write_text(json.dumps(entries,ensure_ascii=False,indent=2),encoding='utf8')
print(f'{len(entries)} complete English/Kazakh/Russian digital UI entries; all literal view keys covered.')
