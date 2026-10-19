namespace EduLibrary.ViewModels;

public sealed class NotificationRowViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string Link { get; set; } = "";
    public string Type { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool IsRead { get; set; }
}

public sealed class NotificationPageViewModel
{
    public IReadOnlyList<NotificationRowViewModel> Items { get; set; } = [];
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
    public int UnreadCount { get; set; }
    public bool UnreadOnly { get; set; }
}

public sealed record NotificationBellViewModel(int UnreadCount, IReadOnlyList<NotificationRowViewModel> Items);
