using EduLibrary.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduLibrary.Data.Configurations;

public sealed class DigitalBookAssetConfiguration : IEntityTypeConfiguration<DigitalBookAsset>
{
    public void Configure(EntityTypeBuilder<DigitalBookAsset> b)
    {
        b.Property(x => x.FileName).HasMaxLength(80).IsRequired();
        b.Property(x => x.StoragePath).HasMaxLength(80).IsRequired();
        b.Property(x => x.OriginalFileName).HasMaxLength(200).IsRequired();
        b.Property(x => x.MimeType).HasMaxLength(80).IsRequired();
        b.Property(x => x.Checksum).HasMaxLength(64).IsRequired();
        b.Property(x => x.Language).HasMaxLength(40).IsRequired();
        b.Property(x => x.LicenseName).HasMaxLength(200).IsRequired();
        b.Property(x => x.LicenseUrl).HasMaxLength(1000);
        b.Property(x => x.SourceUrl).HasMaxLength(1000);
        b.Property(x => x.RightsHolder).HasMaxLength(200).IsRequired();
        b.Property(x => x.UploadedByUserId).HasMaxLength(450).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.Book).WithMany().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.UploadedByUser).WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.StoragePath).IsUnique();
        b.HasIndex(x => new { x.BookId, x.IsPublished, x.IsArchived, x.FileFormat });
        b.HasIndex(x => new { x.IsPublished, x.IsArchived, x.CreatedAt });
        b.ToTable(t => {
            t.HasCheckConstraint("CK_DigitalAssets_Format", "[FileFormat] IN (0,1)");
            t.HasCheckConstraint("CK_DigitalAssets_Access", "[AccessLevel] BETWEEN 0 AND 2");
            t.HasCheckConstraint("CK_DigitalAssets_Size", "[FileSize] > 0 AND [PageCount] >= 0");
            t.HasCheckConstraint("CK_DigitalAssets_License", "[LicenseType] BETWEEN 0 AND 5 AND ([IsPublished] = 0 OR ([RightsConfirmed] = 1 AND [LicenseType] <> 0 AND LEN([LicenseName]) > 0 AND LEN([RightsHolder]) > 0))");
        });
    }
}

public sealed class ReadingProgressConfiguration : IEntityTypeConfiguration<ReadingProgress>
{
    public void Configure(EntityTypeBuilder<ReadingProgress> b)
    {
        b.ToTable("ReadingProgress", t => {
            t.HasCheckConstraint("CK_ReadingProgress_Pages", "[CurrentPage] >= 0 AND [TotalPages] >= 0 AND ([TotalPages] = 0 OR [CurrentPage] <= [TotalPages])");
            t.HasCheckConstraint("CK_ReadingProgress_Percent", "[ProgressPercentage] BETWEEN 0 AND 100");
        });
        b.Property(x => x.UserId).HasMaxLength(450).IsRequired();
        b.Property(x => x.EpubCfi).HasMaxLength(1024);
        b.Property(x => x.ProgressPercentage).HasPrecision(5, 2);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.UserId, x.DigitalBookAssetId }).IsUnique();
        b.HasIndex(x => new { x.UserId, x.LastReadAt });
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        b.HasOne(x => x.DigitalBookAsset).WithMany().HasForeignKey(x => x.DigitalBookAssetId);
    }
}

public sealed class ReadingBookmarkConfiguration : IEntityTypeConfiguration<ReadingBookmark>
{
    public void Configure(EntityTypeBuilder<ReadingBookmark> b)
    {
        b.Property(x => x.UserId).HasMaxLength(450).IsRequired();
        b.Property(x => x.EpubCfi).HasMaxLength(1024);
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Note).HasMaxLength(2000).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.UserId, x.DigitalBookAssetId, x.CreatedAt });
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        b.HasOne(x => x.DigitalBookAsset).WithMany().HasForeignKey(x => x.DigitalBookAssetId);
        b.ToTable(t => t.HasCheckConstraint("CK_ReadingBookmarks_Locator", "([PageNumber] IS NOT NULL AND [PageNumber] > 0) OR ([EpubCfi] IS NOT NULL AND LEN([EpubCfi]) > 0)"));
    }
}

public sealed class DigitalReadingSessionConfiguration : IEntityTypeConfiguration<DigitalReadingSession>
{
    public void Configure(EntityTypeBuilder<DigitalReadingSession> b)
    {
        b.Property(x => x.UserId).HasMaxLength(450).IsRequired();
        b.HasIndex(x => new { x.UserId, x.DigitalBookAssetId, x.LastActivityAt });
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        b.HasOne(x => x.DigitalBookAsset).WithMany().HasForeignKey(x => x.DigitalBookAssetId);
        b.ToTable(t => t.HasCheckConstraint("CK_DigitalReadingSessions_Time", "[LastActivityAt] >= [StartedAt] AND ([CompletedAt] IS NULL OR [CompletedAt] >= [StartedAt])"));
    }
}
