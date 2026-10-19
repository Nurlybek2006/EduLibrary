using EduLibrary.Services.Implementations;
using EduLibrary.Services.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace EduLibrary.Tests;

public sealed class RecommendationContextTests
{
    private readonly RecommendationContextService service=new(new EphemeralDataProtectionProvider(),new Clock());
    [Fact]
    public void OriginalCatalogLinkCarriesActualScoreOnlyForItsViewerAndBook()
    {
        var token=service.Create("reader-one",12,82.75);
        Assert.Equal(82.75,service.Read(token,"reader-one",12)!.Score);
        Assert.Null(service.Read(token,"reader-two",12));
        Assert.Null(service.Read(token,"reader-one",13));
        Assert.Null(service.Read(token,null,12));
    }
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("82.75")]
    [InlineData("untrusted-token")]
    public void QueryTextCannotForgeAPersonalRelevance(string? token)=>Assert.Null(service.Read(token,"reader-one",12));
    [Fact]
    public void TamperingCannotChangeTheScore()
    {
        var token=service.Create("reader-one",12,82.75);
        var characters=token.ToCharArray();var position=characters.Length/2;characters[position]=characters[position]=='A'?'B':'A';
        Assert.Null(service.Read(new string(characters),"reader-one",12));
    }
    private sealed class Clock:ILibraryClock{public DateTime Today=>new(2026,10,20);public DateTime UtcNow=>new(2026,10,20,0,0,0,DateTimeKind.Utc);}
}
