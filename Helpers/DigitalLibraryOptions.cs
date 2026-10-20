namespace EduLibrary.Helpers;

public sealed class DigitalLibraryOptions
{
    public const string SectionName = "DigitalLibrary";
    public string StorageRoot { get; set; } = "App_Data/DigitalLibrary";
    public long MaxFileBytes { get; set; } = 50 * 1024 * 1024;
    public long MaxExpandedEpubBytes { get; set; } = 100 * 1024 * 1024;
    public int MaxEpubEntries { get; set; } = 1000;
    public int MaxBookmarksPerAsset { get; set; } = 200;
    public int SessionIdleMinutes { get; set; } = 30;
    public static bool IsValid(DigitalLibraryOptions o) => !string.IsNullOrWhiteSpace(o.StorageRoot)
        && o.MaxFileBytes is >= 1024 and <= 100 * 1024 * 1024
        && o.MaxExpandedEpubBytes >= o.MaxFileBytes && o.MaxExpandedEpubBytes <= 250 * 1024 * 1024
        && o.MaxEpubEntries is >= 10 and <= 5000 && o.MaxBookmarksPerAsset is >= 1 and <= 1000
        && o.SessionIdleMinutes is >= 5 and <= 240;
}
