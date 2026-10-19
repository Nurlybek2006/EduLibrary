using System.Security.Cryptography;
using System.Text.Json;
using EduLibrary.Services.Interfaces;
using EduLibrary.ViewModels;
using Microsoft.AspNetCore.DataProtection;

namespace EduLibrary.Services.Implementations;

// Carry a short-lived, authentic score to the catalog without rerunning the full
// engine or calling the LLM when a reader opens an already recommended book.
public sealed class RecommendationContextService(IDataProtectionProvider protection,ILibraryClock clock)
{
    private readonly ITimeLimitedDataProtector protector=protection.CreateProtector("EduLibrary.RecommendationContext.v1").ToTimeLimitedDataProtector();
    public string Create(string viewerId,int bookId,double score)
    {
        if(string.IsNullOrEmpty(viewerId)||bookId<=0||!double.IsFinite(score)||score is <0 or >100)throw new ArgumentException("Invalid recommendation context.");
        return protector.Protect(JsonSerializer.Serialize(new Payload(viewerId,bookId,score,clock.UtcNow)),TimeSpan.FromMinutes(15));
    }
    public BookRecommendationContext? Read(string? token,string? viewerId,int bookId)
    {
        if(string.IsNullOrEmpty(token)||token.Length>4096||string.IsNullOrEmpty(viewerId))return null;
        try
        {
            var data=JsonSerializer.Deserialize<Payload>(protector.Unprotect(token,out _));
            return data is not null && data.ViewerId==viewerId && data.BookId==bookId && double.IsFinite(data.Score) && data.Score is >=0 and <=100
                ? new(data.Score,data.CalculatedAt) : null;
        }
        catch(Exception ex) when(ex is CryptographicException or JsonException or FormatException or ArgumentException){return null;}
    }
    private sealed record Payload(string ViewerId,int BookId,double Score,DateTime CalculatedAt);
}

