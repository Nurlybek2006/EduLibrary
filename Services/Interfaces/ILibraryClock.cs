namespace EduLibrary.Services.Interfaces;

public interface ILibraryClock
{
    DateTime Today { get; }
    DateTime UtcNow { get; }
}
