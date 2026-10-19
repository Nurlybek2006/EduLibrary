namespace EduLibrary.Models.Entities;

public class BookSubject
{
    public int BookId { get; set; }
    public int SubjectId { get; set; }
    public Book Book { get; set; } = null!;
    public Subject Subject { get; set; } = null!;
}

public class Notification
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string DeduplicationKey { get; set; } = "";
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string Link { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public ApplicationUser User { get; set; } = null!;
}
