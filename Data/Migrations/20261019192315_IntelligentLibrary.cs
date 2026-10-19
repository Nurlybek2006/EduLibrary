using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduLibrary.Data.Migrations
{
    /// <inheritdoc />
    public partial class IntelligentLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reviews_BookId_StudentId",
                table: "Reviews");

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Reviews",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsHidden",
                table: "Reviews",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Reviews",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Reviews",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BookSubjects",
                columns: table => new
                {
                    BookId = table.Column<int>(type: "int", nullable: false),
                    SubjectId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookSubjects", x => new { x.BookId, x.SubjectId });
                    table.ForeignKey(
                        name: "FK_BookSubjects_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BookSubjects_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    DeduplicationKey = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Link = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.CheckConstraint("CK_Notifications_ReadAt", "[ReadAt] IS NULL OR [ReadAt] >= [CreatedAt]");
                    table.ForeignKey(
                        name: "FK_Notifications_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_BookId_IsDeleted_IsHidden",
                table: "Reviews",
                columns: new[] { "BookId", "IsDeleted", "IsHidden" });

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_BookId_StudentId",
                table: "Reviews",
                columns: new[] { "BookId", "StudentId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Books_IsArchived_Language_PublicationYear",
                table: "Books",
                columns: new[] { "IsArchived", "Language", "PublicationYear" });

            migrationBuilder.CreateIndex(
                name: "IX_BookSubjects_SubjectId",
                table: "BookSubjects",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_DeduplicationKey",
                table: "Notifications",
                column: "DeduplicationKey");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_DeduplicationKey",
                table: "Notifications",
                columns: new[] { "UserId", "DeduplicationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_ReadAt_CreatedAt",
                table: "Notifications",
                columns: new[] { "UserId", "ReadAt", "CreatedAt" });

            // Backfill only new relationship rows; original catalog/history stay intact.
            migrationBuilder.Sql("""
                INSERT INTO BookSubjects (BookId, SubjectId)
                SELECT DISTINCT bc.BookId, s.Id
                FROM BookCategories bc
                JOIN Categories c ON c.Id = bc.CategoryId
                JOIN Subjects s ON s.Name = CASE c.Name
                    WHEN N'Бағдарламалау' THEN N'Бағдарламалау негіздері'
                    WHEN N'Алгоритмдер' THEN N'Деректер құрылымдары'
                    WHEN N'Деректер қоры' THEN N'Деректер қоры'
                    WHEN N'Жасанды интеллект' THEN N'Машиналық оқыту'
                    WHEN N'Желілер' THEN N'Компьютерлік желілер'
                    WHEN N'Математика' THEN N'Жоғары математика'
                END
                WHERE NOT EXISTS (SELECT 1 FROM BookSubjects bs WHERE bs.BookId=bc.BookId AND bs.SubjectId=s.Id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookSubjects");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Reviews_BookId_IsDeleted_IsHidden",
                table: "Reviews");

            migrationBuilder.DropIndex(
                name: "IX_Reviews_BookId_StudentId",
                table: "Reviews");

            migrationBuilder.DropIndex(
                name: "IX_Books_IsArchived_Language_PublicationYear",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "IsHidden",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Reviews");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_BookId_StudentId",
                table: "Reviews",
                columns: new[] { "BookId", "StudentId" },
                unique: true);
        }
    }
}
