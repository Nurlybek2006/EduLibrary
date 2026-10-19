using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduLibrary.Data.Migrations
{
    /// <inheritdoc />
    public partial class OnlineReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Copies_Status",
                table: "BookCopies");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_BookCopies_Id_BookId",
                table: "BookCopies",
                columns: new[] { "Id", "BookId" });

            migrationBuilder.CreateTable(
                name: "Reservations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookId = table.Column<int>(type: "int", nullable: false),
                    StudentId = table.Column<int>(type: "int", nullable: false),
                    BookCopyId = table.Column<int>(type: "int", nullable: true),
                    LoanId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PickupLocationId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    RequestedPickupAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PickupAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReadyAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FulfilledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PickupCode = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    PreparedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    CancelledByUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservations", x => x.Id);
                    table.CheckConstraint("CK_Reservations_Allocation", "([Status] IN (0,1,3) AND [BookCopyId] IS NOT NULL AND [PickupAtUtc] IS NOT NULL AND [ExpiresAtUtc] IS NOT NULL) OR ([Status] = 2 AND [BookCopyId] IS NULL AND [PickupAtUtc] IS NULL AND [ExpiresAtUtc] IS NULL) OR [Status] IN (4,5)");
                    table.CheckConstraint("CK_Reservations_Code", "LEN([PickupCode]) = 36 AND LEFT([PickupCode],4) = 'ELR-' AND SUBSTRING([PickupCode],5,32) COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9A-F]%'");
                    table.CheckConstraint("CK_Reservations_Completed", "([Status] <> 4 OR [CancelledAtUtc] IS NOT NULL) AND ([Status] <> 5 OR [ExpiredAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_Reservations_Dates", "([ReadyAtUtc] IS NULL OR [ReadyAtUtc] >= [CreatedAtUtc]) AND ([ExpiresAtUtc] IS NULL OR [ExpiresAtUtc] >= [CreatedAtUtc]) AND ([PickupAtUtc] IS NULL OR [ExpiresAtUtc] IS NULL OR [ExpiresAtUtc] >= [PickupAtUtc]) AND ([FulfilledAtUtc] IS NULL OR [FulfilledAtUtc] >= [CreatedAtUtc]) AND ([CancelledAtUtc] IS NULL OR [CancelledAtUtc] >= [CreatedAtUtc]) AND ([ExpiredAtUtc] IS NULL OR [ExpiredAtUtc] >= [CreatedAtUtc])");
                    table.CheckConstraint("CK_Reservations_Fulfillment", "([Status] = 3 AND [LoanId] IS NOT NULL AND [FulfilledAtUtc] IS NOT NULL AND [ReadyAtUtc] IS NOT NULL) OR ([Status] <> 3 AND [LoanId] IS NULL AND [FulfilledAtUtc] IS NULL)");
                    table.CheckConstraint("CK_Reservations_Ready", "[Status] <> 1 OR [ReadyAtUtc] IS NOT NULL");
                    table.CheckConstraint("CK_Reservations_Status", "[Status] BETWEEN 0 AND 5");
                    table.ForeignKey(
                        name: "FK_Reservations_AspNetUsers_CancelledByUserId",
                        column: x => x.CancelledByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reservations_AspNetUsers_PreparedByUserId",
                        column: x => x.PreparedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reservations_BookCopies_BookCopyId_BookId",
                        columns: x => new { x.BookCopyId, x.BookId },
                        principalTable: "BookCopies",
                        principalColumns: new[] { "Id", "BookId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reservations_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reservations_Loans_LoanId_StudentId",
                        columns: x => new { x.LoanId, x.StudentId },
                        principalTable: "Loans",
                        principalColumns: new[] { "Id", "StudentId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reservations_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Copies_Status",
                table: "BookCopies",
                sql: "[Status] BETWEEN 0 AND 5");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_BookCopyId_BookId",
                table: "Reservations",
                columns: new[] { "BookCopyId", "BookId" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_BookId_Status_CreatedAtUtc_Id",
                table: "Reservations",
                columns: new[] { "BookId", "Status", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_CancelledByUserId",
                table: "Reservations",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_LoanId",
                table: "Reservations",
                column: "LoanId",
                unique: true,
                filter: "[LoanId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_LoanId_StudentId",
                table: "Reservations",
                columns: new[] { "LoanId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_PickupCode",
                table: "Reservations",
                column: "PickupCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_PickupLocationId_PickupAtUtc_Status",
                table: "Reservations",
                columns: new[] { "PickupLocationId", "PickupAtUtc", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_PreparedByUserId",
                table: "Reservations",
                column: "PreparedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_Status_ExpiresAtUtc",
                table: "Reservations",
                columns: new[] { "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Reservations_ActiveCopy",
                table: "Reservations",
                column: "BookCopyId",
                unique: true,
                filter: "[BookCopyId] IS NOT NULL AND [Status] IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "UX_Reservations_ActiveStudentBook",
                table: "Reservations",
                columns: new[] { "StudentId", "BookId" },
                unique: true,
                filter: "[Status] IN (0, 1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Reservations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_BookCopies_Id_BookId",
                table: "BookCopies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Copies_Status",
                table: "BookCopies");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Copies_Status",
                table: "BookCopies",
                sql: "[Status] BETWEEN 0 AND 4");
        }
    }
}
