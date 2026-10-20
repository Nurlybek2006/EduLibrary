using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduLibrary.Data.Migrations
{
    /// <inheritdoc />
    public partial class MultilingualDigitalLibraryProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AvatarPath",
                table: "AspNetUsers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreferredLanguage",
                table: "AspNetUsers",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "kk-KZ");

            migrationBuilder.AddColumn<string>(
                name: "PreferredTheme",
                table: "AspNetUsers",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "system");

            migrationBuilder.CreateTable(
                name: "DigitalBookAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    FileFormat = table.Column<int>(type: "int", nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    MimeType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Checksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Language = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PageCount = table.Column<int>(type: "int", nullable: false),
                    LicenseType = table.Column<int>(type: "int", nullable: false),
                    LicenseName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LicenseUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SourceUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RightsHolder = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RightsConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    AccessLevel = table.Column<int>(type: "int", nullable: false),
                    AllowDownload = table.Column<bool>(type: "bit", nullable: false),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    UploadedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DigitalBookAssets", x => x.Id);
                    table.CheckConstraint("CK_DigitalAssets_Access", "[AccessLevel] BETWEEN 0 AND 2");
                    table.CheckConstraint("CK_DigitalAssets_Format", "[FileFormat] IN (0,1)");
                    table.CheckConstraint("CK_DigitalAssets_License", "[LicenseType] BETWEEN 0 AND 5 AND ([IsPublished] = 0 OR ([RightsConfirmed] = 1 AND [LicenseType] <> 0 AND LEN([LicenseName]) > 0 AND LEN([RightsHolder]) > 0))");
                    table.CheckConstraint("CK_DigitalAssets_Size", "[FileSize] > 0 AND [PageCount] >= 0");
                    table.ForeignKey(
                        name: "FK_DigitalBookAssets_AspNetUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DigitalBookAssets_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DigitalReadingSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    DigitalBookAssetId = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DigitalReadingSessions", x => x.Id);
                    table.CheckConstraint("CK_DigitalReadingSessions_Time", "[LastActivityAt] >= [StartedAt] AND ([CompletedAt] IS NULL OR [CompletedAt] >= [StartedAt])");
                    table.ForeignKey(
                        name: "FK_DigitalReadingSessions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DigitalReadingSessions_DigitalBookAssets_DigitalBookAssetId",
                        column: x => x.DigitalBookAssetId,
                        principalTable: "DigitalBookAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReadingBookmarks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    DigitalBookAssetId = table.Column<int>(type: "int", nullable: false),
                    PageNumber = table.Column<int>(type: "int", nullable: true),
                    EpubCfi = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingBookmarks", x => x.Id);
                    table.CheckConstraint("CK_ReadingBookmarks_Locator", "([PageNumber] IS NOT NULL AND [PageNumber] > 0) OR ([EpubCfi] IS NOT NULL AND LEN([EpubCfi]) > 0)");
                    table.ForeignKey(
                        name: "FK_ReadingBookmarks_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReadingBookmarks_DigitalBookAssets_DigitalBookAssetId",
                        column: x => x.DigitalBookAssetId,
                        principalTable: "DigitalBookAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReadingProgress",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    DigitalBookAssetId = table.Column<int>(type: "int", nullable: false),
                    CurrentPage = table.Column<int>(type: "int", nullable: false),
                    TotalPages = table.Column<int>(type: "int", nullable: false),
                    EpubCfi = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    ProgressPercentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    LastReadAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingProgress", x => x.Id);
                    table.CheckConstraint("CK_ReadingProgress_Pages", "[CurrentPage] >= 0 AND [TotalPages] >= 0 AND ([TotalPages] = 0 OR [CurrentPage] <= [TotalPages])");
                    table.CheckConstraint("CK_ReadingProgress_Percent", "[ProgressPercentage] BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_ReadingProgress_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReadingProgress_DigitalBookAssets_DigitalBookAssetId",
                        column: x => x.DigitalBookAssetId,
                        principalTable: "DigitalBookAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_Language",
                table: "AspNetUsers",
                sql: "[PreferredLanguage] IN ('kk-KZ','ru-RU','en-US')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_Theme",
                table: "AspNetUsers",
                sql: "[PreferredTheme] IN ('system','light','dark')");

            migrationBuilder.CreateIndex(
                name: "IX_DigitalBookAssets_BookId_IsPublished_IsArchived_FileFormat",
                table: "DigitalBookAssets",
                columns: new[] { "BookId", "IsPublished", "IsArchived", "FileFormat" });

            migrationBuilder.CreateIndex(
                name: "IX_DigitalBookAssets_IsPublished_IsArchived_CreatedAt",
                table: "DigitalBookAssets",
                columns: new[] { "IsPublished", "IsArchived", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DigitalBookAssets_StoragePath",
                table: "DigitalBookAssets",
                column: "StoragePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DigitalBookAssets_UploadedByUserId",
                table: "DigitalBookAssets",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DigitalReadingSessions_DigitalBookAssetId",
                table: "DigitalReadingSessions",
                column: "DigitalBookAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_DigitalReadingSessions_UserId_DigitalBookAssetId_LastActivityAt",
                table: "DigitalReadingSessions",
                columns: new[] { "UserId", "DigitalBookAssetId", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingBookmarks_DigitalBookAssetId",
                table: "ReadingBookmarks",
                column: "DigitalBookAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingBookmarks_UserId_DigitalBookAssetId_CreatedAt",
                table: "ReadingBookmarks",
                columns: new[] { "UserId", "DigitalBookAssetId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingProgress_DigitalBookAssetId",
                table: "ReadingProgress",
                column: "DigitalBookAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingProgress_UserId_DigitalBookAssetId",
                table: "ReadingProgress",
                columns: new[] { "UserId", "DigitalBookAssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReadingProgress_UserId_LastReadAt",
                table: "ReadingProgress",
                columns: new[] { "UserId", "LastReadAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DigitalReadingSessions");

            migrationBuilder.DropTable(
                name: "ReadingBookmarks");

            migrationBuilder.DropTable(
                name: "ReadingProgress");

            migrationBuilder.DropTable(
                name: "DigitalBookAssets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_Language",
                table: "AspNetUsers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_Theme",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "AvatarPath",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PreferredLanguage",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PreferredTheme",
                table: "AspNetUsers");
        }
    }
}
