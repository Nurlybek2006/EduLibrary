using EduLibrary.Helpers;
using EduLibrary.Data;
using EduLibrary.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduLibrary.Tests;

public sealed class ReviewTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void InvalidRatingIsRejectedBeforePersistence(int rating) =>
        Assert.Throws<BusinessException>(() => ReviewPolicy.ValidateContent(rating, "Пікір"));

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void RatingBoundariesAllowAnOptionalComment(int rating) => ReviewPolicy.ValidateContent(rating, null);

    [Fact]
    public void OverlongCommentIsRejected() =>
        Assert.Throws<BusinessException>(() => ReviewPolicy.ValidateContent(4, new string('а', 3001)));

    [Fact]
    public void DatabaseAllowsAnotherReviewOnlyAfterSoftDeletion()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=localhost;Database=ModelOnly;Integrated Security=true;TrustServerCertificate=true").Options);
        var entity = db.Model.FindEntityType(typeof(Review))!;
        var index = entity.GetIndexes().Single(x => x.Properties.Select(p => p.Name).SequenceEqual(new[] { "BookId", "StudentId" }));
        Assert.True(index.IsUnique);
        Assert.Equal("[IsDeleted] = 0", index.GetFilter());
        Assert.True(entity.FindProperty(nameof(Review.RowVersion))!.IsConcurrencyToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("malformed")]
    [InlineData("AA==")]
    public void MissingOrMalformedRowVersionIsRejected(string? value) =>
        Assert.Throws<BusinessException>(() => ReviewPolicy.ParseVersion(value));

    [Fact]
    public void EightByteSqlRowVersionIsPreserved()
    {
        byte[] expected = [0, 0, 0, 0, 0, 0, 1, 42];
        Assert.Equal(expected, ReviewPolicy.ParseVersion(Convert.ToBase64String(expected)));
    }
}
