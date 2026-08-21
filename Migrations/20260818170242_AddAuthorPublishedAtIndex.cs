using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace explAInedArticleService.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorPublishedAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Articles_AuthorId_PublishedAt",
                table: "Articles",
                columns: new[] { "AuthorId", "PublishedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Articles_AuthorId_PublishedAt",
                table: "Articles");
        }
    }
}
