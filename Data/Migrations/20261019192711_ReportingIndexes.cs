using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduLibrary.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReportingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Reviews_IsDeleted_IsHidden_CreatedAt",
                table: "Reviews",
                columns: new[] { "IsDeleted", "IsHidden", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Returns_ReturnDate",
                table: "Returns",
                column: "ReturnDate");

            migrationBuilder.CreateIndex(
                name: "IX_Loans_LoanDate",
                table: "Loans",
                column: "LoanDate");

            migrationBuilder.CreateIndex(
                name: "IX_FinePayments_PaidAt",
                table: "FinePayments",
                column: "PaidAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reviews_IsDeleted_IsHidden_CreatedAt",
                table: "Reviews");

            migrationBuilder.DropIndex(
                name: "IX_Returns_ReturnDate",
                table: "Returns");

            migrationBuilder.DropIndex(
                name: "IX_Loans_LoanDate",
                table: "Loans");

            migrationBuilder.DropIndex(
                name: "IX_FinePayments_PaidAt",
                table: "FinePayments");
        }
    }
}
