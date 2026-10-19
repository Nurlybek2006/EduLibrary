using System.Net;

namespace EduLibrary.Helpers;

public sealed class RecommendationOptions
{
    public const string SectionName = "Recommendations";
    public double InterestWeight { get; set; } = 30;
    public double SubjectWeight { get; set; } = 25;
    public double HistoryWeight { get; set; } = 25;
    public double PopularityWeight { get; set; } = 20;
    public bool ExcludePreviouslyBorrowed { get; set; } = true;
    public bool PrioritizeAvailable { get; set; } = true;
    public int CandidateLimit { get; set; } = 2000;
    public int ItemsPerSection { get; set; } = 4;
    public RecommendationLlmOptions Llm { get; set; } = new();

    public static bool IsValid(RecommendationOptions options)
    {
        var weights = new[] { options.InterestWeight, options.SubjectWeight, options.HistoryWeight, options.PopularityWeight };
        return weights.All(w => double.IsFinite(w) && w is >= 0 and <= 1000) && weights.Sum() > 0
            && options.CandidateLimit is >= 50 and <= 10000 && options.ItemsPerSection is >= 1 and <= 12;
    }

    public static bool TryGetEndpoint(string? baseUrl, out Uri? endpoint)
    {
        endpoint = null;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || uri.IsLoopback || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)) return false;
        if (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip))
        {
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            var bytes = ip.GetAddressBytes();
            if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal
                || bytes.Length == 4 && (bytes[0] is 0 or 10 or 127 || bytes[0] == 169 && bytes[1] == 254 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168)
                || bytes.Length == 16 && (bytes[0] & 0xfe) == 0xfc) return false;
        }
        endpoint = uri.AbsolutePath.TrimEnd('/').EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? new Uri(uri.AbsoluteUri.TrimEnd('/')) : new Uri(uri.AbsoluteUri.TrimEnd('/') + "/chat/completions");
        return true;
    }
}

public sealed class RecommendationLlmOptions
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 8;
    public int MaxExplanations { get; set; } = 12;
}
