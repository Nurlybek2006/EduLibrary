using System.IO.Compression;
using System.Text;
using EduLibrary.Helpers;
using EduLibrary.Models.Enums;
using EduLibrary.Services.Implementations;
using EduLibrary.ViewModels;

namespace EduLibrary.Tests;

public sealed class DigitalLibraryRulesTests
{
    [Theory]
    [InlineData("csharp-basics")]
    [InlineData("sql-introduction")]
    [InlineData("algorithms-structures")]
    [InlineData("web-fundamentals")]
    [InlineData("cybersecurity-basics")]
    public void All_original_demo_documents_pass_the_production_validators(string slug)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EduLibrary.csproj"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var root = Path.Combine(directory.FullName, "Data", "DigitalDemo", slug);
        Assert.Equal(8, DigitalFileRules.ValidatePdf(File.ReadAllBytes(root + ".pdf")));
        var epub = DigitalFileRules.SanitizeEpub(File.ReadAllBytes(root + ".epub"), new());
        using var zip = new ZipArchive(new MemoryStream(epub));
        Assert.NotNull(zip.GetEntry("META-INF/container.xml"));
        Assert.Contains(zip.Entries, entry => entry.Name.EndsWith(".xhtml", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("aabbccddeeff00112233445566778899.pdf", true)]
    [InlineData("aabbccddeeff00112233445566778899.epub", true)]
    [InlineData("../../secret.pdf", false)]
    [InlineData("C:\\secret.pdf", false)]
    [InlineData("test.pdf", false)]
    [InlineData("aabbccddeeff00112233445566778899.exe", false)]
    public void Storage_names_are_random_leaf_names_only(string value, bool valid) => Assert.Equal(valid, DigitalFileRules.IsSafeLeaf(value));

    [Theory]
    [InlineData("/digital-demo/provenance.html", true)]
    [InlineData("https://creativecommons.org/licenses/by/4.0/", true)]
    [InlineData("//evil.example/a", false)]
    [InlineData("/../secret", false)]
    [InlineData("/%2e%2e/secret", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,x", false)]
    [InlineData("https://name:password@example.com/", false)]
    public void Provenance_accepts_real_local_sources_and_https_without_active_links(string value, bool valid)
        => Assert.Equal(valid, DigitalFileRules.IsSafeSourceUrl(value));

    [Theory]
    [InlineData("epubcfi(/6/2[chapter-1]!/4/2/1:0)", true)]
    [InlineData("epubcfi(/6/2!/4/2:18)", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("epubcfi(<script>)", false)]
    [InlineData("epubcfi(../../secret)", false)]
    public void Epub_positions_are_bounded_cfi_locators(string value, bool valid) => Assert.Equal(valid, DigitalFileRules.IsValidCfi(value));

    [Fact]
    public void Published_assets_require_confirmed_complete_provenance()
    {
        var model = new DigitalAssetEditViewModel { IsPublished = true, Language = "English", LicenseType = DigitalLicenseType.Unknown };
        Assert.Throws<BusinessException>(() => DigitalLibraryService.ValidateMetadata(model));
        model.LicenseType = DigitalLicenseType.Original; model.LicenseName = "CC BY 4.0"; model.LicenseUrl = "https://creativecommons.org/licenses/by/4.0/";
        model.RightsHolder = "EduLibrary Demo Content"; model.SourceUrl = "/digital-demo/provenance.html";
        Assert.Throws<BusinessException>(() => DigitalLibraryService.ValidateMetadata(model));
        model.RightsConfirmed = true;
        DigitalLibraryService.ValidateMetadata(model);
        model.IsPublished = false; model.LicenseType = DigitalLicenseType.Unknown; model.RightsConfirmed = false;
        model.LicenseName = null; model.RightsHolder = null; model.LicenseUrl = null; model.SourceUrl = null;
        DigitalLibraryService.ValidateMetadata(model); // Unknown rights may be saved only as a draft.
    }

    [Fact]
    public void Pdf_progress_must_match_server_detected_page_count()
    {
        ReadingProgressService.ValidateLocator(DigitalFileFormat.Pdf, 8, 3, 8, null, 99);
        Assert.Throws<BusinessException>(() => ReadingProgressService.ValidateLocator(DigitalFileFormat.Pdf, 8, 9, 9, null, 99));
        Assert.Throws<BusinessException>(() => ReadingProgressService.ValidateLocator(DigitalFileFormat.Pdf, 8, 3, 10000, null, 10));
        Assert.Throws<BusinessException>(() => ReadingProgressService.ValidateLocator(DigitalFileFormat.Pdf, 8, 0, 8, null, 0));
        Assert.Throws<BusinessException>(() => ReadingProgressService.ValidateLocator(DigitalFileFormat.Epub, 0, 0, 0, null, 50));
    }

    [Theory]
    [InlineData("/JavaScript")]
    [InlineData("/#4A#53")]
    [InlineData("/Launch")]
    [InlineData("/EmbeddedFile")]
    [InlineData("/ObjStm")]
    [InlineData("/Encrypt")]
    [InlineData("/AcroForm")]
    public void Pdf_active_actions_and_obfuscated_names_are_rejected(string action)
    {
        var input = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Page " + action + " >> endobj\n%%EOF");
        Assert.Throws<BusinessException>(() => DigitalFileRules.ValidatePdf(input));
    }

    [Fact]
    public void Epub_is_rebuilt_without_scripts_styles_forms_svg_or_external_references()
    {
        var bytes = Epub("<script>alert(1)</script><p id='one' onclick='x()' style='background:url(https://evil.example)'>Safe lesson</p><a href='javascript:alert(1)'>bad</a><img src='https://evil.example/track.png'/><iframe src='https://evil.example'/><svg xmlns='http://www.w3.org/2000/svg'><script>x()</script></svg><a href='chapter.xhtml#one'>Chapter</a>");
        var sanitized = DigitalFileRules.SanitizeEpub(bytes, new());
        using var archive = new ZipArchive(new MemoryStream(sanitized));
        using var reader = new StreamReader(archive.GetEntry("EPUB/chapter.xhtml")!.Open());
        var text = reader.ReadToEnd();
        Assert.Contains("Safe lesson", text);
        Assert.Contains("chapter.xhtml#one", text);
        Assert.Contains("Content-Security-Policy", text);
        Assert.DoesNotContain("<script", text); Assert.DoesNotContain("onclick", text); Assert.DoesNotContain("<iframe", text);
        Assert.DoesNotContain("<svg", text); Assert.DoesNotContain("evil.example", text); Assert.DoesNotContain("javascript:", text);
        Assert.DoesNotContain(archive.Entries, entry => entry.Name.EndsWith(".js", StringComparison.Ordinal));
    }

    [Fact]
    public void Epub_rejects_zip_traversal_duplicate_names_xml_entities_and_expansion_limits()
    {
        Assert.Throws<BusinessException>(() => DigitalFileRules.SanitizeEpub(Epub("<p>Lesson</p>", "../escape.xhtml"), new()));
        Assert.Throws<BusinessException>(() => DigitalFileRules.SanitizeEpub(Epub("<p>Lesson</p>", "EPUB/chapter.xhtml"), new()));
        Assert.Throws<BusinessException>(() => DigitalFileRules.SanitizeEpub(Epub("<p>Lesson</p>", null, "<!DOCTYPE container [<!ENTITY x SYSTEM 'file:///secret'>]><container>&x;</container>"), new()));
        Assert.Throws<BusinessException>(() => DigitalFileRules.SanitizeEpub(Epub("<p>" + new string('A', 20000) + "</p>"), new() { MaxExpandedEpubBytes = 1000 }));
    }

    internal static byte[] Epub(string body, string? extraPath = null, string? container = null)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            Add(zip, "mimetype", "application/epub+zip");
            Add(zip, "META-INF/container.xml", container ?? "<container xmlns='urn:oasis:names:tc:opendocument:xmlns:container' version='1.0'><rootfiles><rootfile full-path='EPUB/package.opf' media-type='application/oebps-package+xml'/></rootfiles></container>");
            Add(zip, "EPUB/package.opf", "<package xmlns='http://www.idpf.org/2007/opf' version='3.0'><metadata xmlns:dc='http://purl.org/dc/elements/1.1/'><dc:title>Original lesson</dc:title><dc:language>en</dc:language><dc:identifier>urn:test</dc:identifier></metadata><manifest><item id='chapter' href='chapter.xhtml' media-type='application/xhtml+xml'/><item id='script' href='bad.js' media-type='application/javascript'/></manifest><spine><itemref idref='chapter'/></spine></package>");
            Add(zip, "EPUB/chapter.xhtml", "<html xmlns='http://www.w3.org/1999/xhtml'><head><title>Lesson</title></head><body>" + body + "</body></html>");
            Add(zip, "EPUB/bad.js", "alert(document.cookie)");
            if (extraPath is not null) Add(zip, extraPath, "extra");
        }
        return stream.ToArray();
    }
    private static void Add(ZipArchive archive, string name, string value) { using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false)); writer.Write(value); }
}
