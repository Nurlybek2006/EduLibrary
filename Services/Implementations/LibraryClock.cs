using EduLibrary.Services.Interfaces;

namespace EduLibrary.Services.Implementations;

public class LibraryClock : ILibraryClock
{
    private static readonly TimeZoneInfo LibraryTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Qyzylorda");
    public DateTime UtcNow => DateTime.UtcNow;
    public DateTime Today => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, LibraryTimeZone).Date;
}
