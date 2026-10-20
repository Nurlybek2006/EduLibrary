using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using EduLibrary.Models.Enums;

namespace EduLibrary.Helpers;

/// <summary>Conservative document validation. EPUB output is a new passive package, never the uploaded bytes.</summary>
public static partial class DigitalFileRules
{
    public static string MimeType(DigitalFileFormat format) => format == DigitalFileFormat.Pdf ? "application/pdf" : "application/epub+zip";
    public static string Extension(DigitalFileFormat format) => format == DigitalFileFormat.Pdf ? ".pdf" : ".epub";
    public static bool IsSafeLeaf(string? value) => value is not null && SafeLeaf().IsMatch(value);
    public static bool IsSafeWebUrl(string? value) => string.IsNullOrWhiteSpace(value)
        || Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) && !string.IsNullOrEmpty(uri.Host) && value.Length <= 1000;
    public static bool IsSafeSourceUrl(string? value) => IsSafeWebUrl(value) || value is not null && value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal)
        && value.Length <= 1000 && !value.Contains("..", StringComparison.Ordinal) && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_' or '.');

    public static int ValidatePdf(byte[] bytes)
    {
        if (bytes.Length < 32 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8)) throw new BusinessException(UiText.Get("Digital.InvalidPdf"));
        var content = Encoding.Latin1.GetString(bytes);
        var rawContent = content;
        if (!content.AsSpan(Math.Max(0, content.Length - 1024)).Contains("%%EOF", StringComparison.Ordinal)) throw new BusinessException(UiText.Get("Digital.InvalidPdf"));
        // PDF names may use #HH escapes. Actions cannot hide inside object streams because those are rejected.
        content = PdfNameEscape().Replace(content, m => ((char)Convert.ToByte(m.Groups[1].Value, 16)).ToString());
        if (ActivePdf().IsMatch(content)) throw new BusinessException(UiText.Get("Digital.UnsafePdf"));
        var crossReference = PdfCrossReference().Matches(rawContent).LastOrDefault();
        if (crossReference is null || !long.TryParse(crossReference.Groups[1].Value, out var offset) || offset < 0 || offset > bytes.LongLength - 4
            || !rawContent.AsSpan((int)offset, 4).SequenceEqual("xref") || !content.Contains("/Root", StringComparison.Ordinal) || !content.Contains("/Catalog", StringComparison.Ordinal))
            throw new BusinessException(UiText.Get("Digital.InvalidPdf"));
        var pages = PdfPage().Count(content);
        if (pages is < 1 or > 100000) throw new BusinessException(UiText.Get("Digital.InvalidPdf"));
        return pages;
    }

    public static bool IsValidCfi(string? value) => value is not null && value.Length is >= 12 and <= 1024
        && value.StartsWith("epubcfi(", StringComparison.Ordinal) && value.EndsWith(')') && CfiCharacters().IsMatch(value)
        && !value.Contains("..", StringComparison.Ordinal);

    public static byte[] SanitizeEpub(byte[] bytes, DigitalLibraryOptions settings)
    {
        try { return SanitizePackage(bytes, settings); }
        catch (BusinessException) { throw; }
        catch (Exception error) when (error is InvalidDataException or XmlException or ArgumentException or IOException or OverflowException)
        { throw new BusinessException(UiText.Get("Digital.InvalidEpub")); }
    }

    private static byte[] SanitizePackage(byte[] bytes, DigitalLibraryOptions settings)
    {
        if (bytes.Length < 30 || !bytes.AsSpan(0, 4).SequenceEqual("PK\x03\x04"u8)) throw new InvalidDataException();
        using var input = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        if (archive.Entries.Count > settings.MaxEpubEntries) throw new InvalidDataException();
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/')) continue;
            if (!SafePackagePath(entry.FullName) || entries.ContainsKey(entry.FullName) || entry.Length > settings.MaxExpandedEpubBytes) throw new InvalidDataException();
            total = checked(total + entry.Length);
            if (total > settings.MaxExpandedEpubBytes || entry.Length > 1024 * 1024 && entry.CompressedLength * 200 < entry.Length) throw new InvalidDataException();
            using var stream = entry.Open(); using var buffer = new MemoryStream();
            var block = new byte[81920]; int read;
            while ((read = stream.Read(block, 0, block.Length)) != 0)
            {
                if (buffer.Length + read > entry.Length || buffer.Length + read > settings.MaxExpandedEpubBytes) throw new InvalidDataException();
                buffer.Write(block, 0, read);
            }
            if (buffer.Length != entry.Length) throw new InvalidDataException();
            entries.Add(entry.FullName, buffer.ToArray());
        }
        if (!entries.TryGetValue("mimetype", out var mime) || Encoding.ASCII.GetString(mime) != "application/epub+zip"
            || !entries.TryGetValue("META-INF/container.xml", out var containerBytes) || entries.ContainsKey("META-INF/encryption.xml")) throw new InvalidDataException();
        var container = ParseXml(containerBytes);
        var packagePath = container.Descendants().FirstOrDefault(e => e.Name.LocalName == "rootfile")?.Attribute("full-path")?.Value;
        if (packagePath is null || !SafePackagePath(packagePath) || !entries.TryGetValue(packagePath, out var packageBytes)) throw new InvalidDataException();
        var original = ParseXml(packageBytes);
        if (original.Root?.Name.LocalName != "package") throw new InvalidDataException();
        var rootFolder = packagePath.Contains('/') ? packagePath[..(packagePath.LastIndexOf('/') + 1)] : "";
        var manifest = new List<EpubItem>();
        foreach (var item in original.Descendants().Where(e => e.Name.LocalName == "manifest").Elements().Where(e => e.Name.LocalName == "item"))
        {
            var id = item.Attribute("id")?.Value; var href = item.Attribute("href")?.Value; var media = item.Attribute("media-type")?.Value;
            if (id is null || !SafeIdentifier().IsMatch(id) || href is null || media is null) continue;
            var full = ResolveReference(rootFolder, href);
            if (full is null || !entries.ContainsKey(full) || manifest.Any(i => i.Id == id || i.Path == full)) continue;
            if (media is not ("application/xhtml+xml" or "application/x-dtbncx+xml" or "image/png" or "image/jpeg")) continue;
            manifest.Add(new(id, href, full, media, item.Attribute("properties")?.Value.Split(' ').Contains("nav") == true));
        }
        var readable = manifest.Where(i => i.Media == "application/xhtml+xml").Select(i => i.Path).ToHashSet(StringComparer.Ordinal);
        var resources = manifest.Select(i => i.Path).ToHashSet(StringComparer.Ordinal);
        var spineIds = original.Descendants().Where(e => e.Name.LocalName == "spine").Elements().Where(e => e.Name.LocalName == "itemref")
            .Select(e => e.Attribute("idref")?.Value).Where(id => manifest.Any(i => i.Id == id && readable.Contains(i.Path))).Cast<string>().Distinct().ToList();
        if (spineIds.Count == 0) throw new InvalidDataException();
        var sanitized = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var item in manifest)
        {
            var data = entries[item.Path];
            if (item.Media == "application/xhtml+xml") sanitized[item.Path] = SanitizeXhtml(data, item.Path, resources);
            else if (item.Media == "application/x-dtbncx+xml") sanitized[item.Path] = SanitizeNcx(data, item.Path, readable);
            else
            {
                if (!SafeImage(data, item.Media)) throw new InvalidDataException();
                sanitized[item.Path] = data;
            }
        }
        XNamespace opf = "http://www.idpf.org/2007/opf", dc = "http://purl.org/dc/elements/1.1/";
        var metadata = new XElement(opf + "metadata", new XAttribute(XNamespace.Xmlns + "dc", dc));
        foreach (var name in new[] { "title", "creator", "language", "identifier", "description", "rights" })
        {
            var value = original.Descendants().FirstOrDefault(e => e.Name.Namespace == dc && e.Name.LocalName == name)?.Value;
            if (string.IsNullOrWhiteSpace(value)) value = name == "identifier" ? "urn:uuid:" + Guid.NewGuid() : name == "title" ? "Digital publication" : name == "language" ? "en" : null;
            if (value is not null) metadata.Add(new XElement(dc + name, name == "identifier" ? new XAttribute("id", "book-id") : null, value[..Math.Min(value.Length, 4000)]));
        }
        var version = original.Root.Attribute("version")?.Value.StartsWith("3", StringComparison.Ordinal) == true ? "3.0" : "2.0";
        if (version == "3.0") metadata.Add(new XElement(opf + "meta", new XAttribute("property", "dcterms:modified"), DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")));
        var newManifest = new XElement(opf + "manifest", manifest.Select(i => new XElement(opf + "item", new XAttribute("id", i.Id), new XAttribute("href", i.Href), new XAttribute("media-type", i.Media), i.Nav ? new XAttribute("properties", "nav") : null)));
        var ncx = manifest.FirstOrDefault(i => i.Media == "application/x-dtbncx+xml");
        var spine = new XElement(opf + "spine", ncx is null ? null : new XAttribute("toc", ncx.Id), spineIds.Select(id => new XElement(opf + "itemref", new XAttribute("idref", id))));
        sanitized[packagePath] = XmlBytes(new XDocument(new XElement(opf + "package", new XAttribute("version", version), new XAttribute("unique-identifier", "book-id"), metadata, newManifest, spine)));
        XNamespace cn = "urn:oasis:names:tc:opendocument:xmlns:container";
        sanitized["META-INF/container.xml"] = XmlBytes(new XDocument(new XElement(cn + "container", new XAttribute("version", "1.0"), new XElement(cn + "rootfiles", new XElement(cn + "rootfile", new XAttribute("full-path", packagePath), new XAttribute("media-type", "application/oebps-package+xml"))))));
        using var result = new MemoryStream();
        using (var output = new ZipArchive(result, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(output, "mimetype", "application/epub+zip"u8.ToArray(), CompressionLevel.NoCompression);
            foreach (var item in sanitized) WriteEntry(output, item.Key, item.Value, CompressionLevel.Optimal);
        }
        return result.ToArray();
    }

    private static byte[] SanitizeXhtml(byte[] input, string path, HashSet<string> resources)
    {
        var original = ParseXml(input);
        if (original.Root?.Name.LocalName != "html") throw new InvalidDataException();
        XNamespace html = "http://www.w3.org/1999/xhtml", epub = "http://www.idpf.org/2007/ops";
        var allowed = new HashSet<string>("html head title body main article section header footer nav h1 h2 h3 h4 h5 h6 p div span a img ol ul li dl dt dd blockquote pre code em strong b i u s small sub sup br hr table thead tbody tfoot tr td th caption figure figcaption".Split(' '), StringComparer.Ordinal);
        XElement? Clean(XElement element)
        {
            var name = element.Name.LocalName.ToLowerInvariant();
            if (!allowed.Contains(name) || element.Name.NamespaceName is not ("" or "http://www.w3.org/1999/xhtml")) return null;
            var clean = new XElement(html + name);
            foreach (var attr in element.Attributes().Where(a => !a.IsNamespaceDeclaration))
            {
                var key = attr.Name.LocalName.ToLowerInvariant(); var value = attr.Value;
                if (value.Length > 2048 || value.Any(char.IsControl)) continue;
                if (key is "id" or "class" or "title" or "lang" or "dir" or "role" or "aria-label" or "alt") clean.SetAttributeValue(key, value);
                else if (attr.Name == epub + "type") clean.SetAttributeValue(epub + "type", value);
                else if (name is "td" or "th" && key is "colspan" or "rowspan" && int.TryParse(value, out var number) && number is >= 1 and <= 1000) clean.SetAttributeValue(key, number);
                else if (name == "a" && key == "href" || name == "img" && key == "src")
                {
                    var folder = path.Contains('/') ? path[..(path.LastIndexOf('/') + 1)] : "";
                    var resolved = ResolveReference(folder, value);
                    if (value.StartsWith('#') || resolved is not null && resources.Contains(resolved)) clean.SetAttributeValue(key, value);
                }
            }
            foreach (var node in element.Nodes())
                if (node is XElement child) { var safe = Clean(child); if (safe is not null) clean.Add(safe); }
                else if (node is XText text) clean.Add(new XText(text.Value));
            return clean;
        }
        var root = Clean(original.Root)!;
        var head = root.Element(html + "head");
        if (head is null) { head = new XElement(html + "head"); root.AddFirst(head); }
        head.Add(new XElement(html + "meta", new XAttribute("http-equiv", "Content-Security-Policy"), new XAttribute("content", "default-src 'none'; script-src 'none'; img-src blob: data:; style-src 'unsafe-inline'; form-action 'none'; base-uri 'none'")));
        return XmlBytes(new XDocument(root));
    }

    private static byte[] SanitizeNcx(byte[] input, string path, HashSet<string> readable)
    {
        var original = ParseXml(input); XNamespace ncx = "http://www.daisy.org/z3986/2005/ncx/";
        var folder = path.Contains('/') ? path[..(path.LastIndexOf('/') + 1)] : "";
        var map = new XElement(ncx + "navMap"); var index = 0;
        foreach (var item in original.Descendants().Where(e => e.Name.LocalName == "navPoint"))
        {
            var source = item.Descendants().FirstOrDefault(e => e.Name.LocalName == "content")?.Attribute("src")?.Value;
            if (source is null || ResolveReference(folder, source) is not string resolved || !readable.Contains(resolved)) continue;
            var label = item.Descendants().FirstOrDefault(e => e.Name.LocalName == "text")?.Value ?? (++index).ToString();
            map.Add(new XElement(ncx + "navPoint", new XAttribute("id", "n" + ++index), new XAttribute("playOrder", index), new XElement(ncx + "navLabel", new XElement(ncx + "text", label)), new XElement(ncx + "content", new XAttribute("src", source))));
        }
        return XmlBytes(new XDocument(new XElement(ncx + "ncx", new XAttribute("version", "2005-1"), new XElement(ncx + "head"), new XElement(ncx + "docTitle", new XElement(ncx + "text", "Contents")), map)));
    }

    private static XDocument ParseXml(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8 * 1024 * 1024, MaxCharactersFromEntities = 0 };
        using (var scan = XmlReader.Create(stream, settings))
            while (scan.Read()) if (scan.Depth > 80) throw new XmlException("Document nesting exceeds the safe limit.");
        stream.Position = 0;
        using var reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader, LoadOptions.None);
    }
    private static byte[] XmlBytes(XDocument document) { using var result = new MemoryStream(); using (var writer = XmlWriter.Create(result, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false })) document.Save(writer); return result.ToArray(); }
    private static void WriteEntry(ZipArchive zip, string name, byte[] bytes, CompressionLevel level) { using var stream = zip.CreateEntry(name, level).Open(); stream.Write(bytes); }
    public static bool SafePackagePath(string path) => path.Length is > 0 and <= 300 && !path.StartsWith('/') && !path.Contains('\\') && !path.Contains(':') && !path.Contains('%') && !path.Any(char.IsControl) && path.Split('/').All(s => s.Length > 0 && s is not "." and not "..");
    private static string? ResolveReference(string folder, string reference)
    {
        var value = reference.Split('#')[0];
        if (value.Length == 0) return null;
        if (value.Contains('%') || value.Contains('\\') || value.Contains(':') || value.Contains('?') || value.StartsWith('/') || value.Any(char.IsControl)) return null;
        var segments = new List<string>(folder.Split('/', StringSplitOptions.RemoveEmptyEntries));
        foreach (var part in value.Split('/'))
        {
            if (part is "" or ".") continue;
            if (part == "..") { if (segments.Count == 0) return null; segments.RemoveAt(segments.Count - 1); }
            else segments.Add(part);
        }
        var combined = string.Join('/', segments);
        return SafePackagePath(combined) ? combined : null;
    }
    private static bool SafeImage(byte[] data, string mime) => mime == "image/png"
        ? data.Length >= 24 && data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) && System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(16, 4)) is > 0 and <= 10000 && System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(20, 4)) is > 0 and <= 10000
        : data.Length >= 4 && data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff;
    private sealed record EpubItem(string Id, string Href, string Path, string Media, bool Nav);
    [GeneratedRegex("^[a-f0-9]{32}\\.(pdf|epub)$", RegexOptions.CultureInvariant)] private static partial Regex SafeLeaf();
    [GeneratedRegex("#([0-9a-fA-F]{2})", RegexOptions.CultureInvariant)] private static partial Regex PdfNameEscape();
    [GeneratedRegex(@"/(JavaScript|JS|Launch|EmbeddedFile|OpenAction|AA|RichMedia|SubmitForm|GoToR|XFA|Encrypt|AcroForm|ObjStm)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)] private static partial Regex ActivePdf();
    [GeneratedRegex(@"/Type\s*/Page\b", RegexOptions.CultureInvariant)] private static partial Regex PdfPage();
    [GeneratedRegex(@"startxref\s+([0-9]+)\s+%%EOF", RegexOptions.CultureInvariant)] private static partial Regex PdfCrossReference();
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.-]{0,79}$", RegexOptions.CultureInvariant)] private static partial Regex SafeIdentifier();
    [GeneratedRegex(@"^epubcfi\([A-Za-z0-9_\[\]/!:,;.=^()%+~@\- ]+\)$", RegexOptions.CultureInvariant)] private static partial Regex CfiCharacters();
}
