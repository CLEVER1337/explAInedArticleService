using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace explAInedArticleService.Migrations
{
    /// <inheritdoc />
    public partial class AddStatusPublishedAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Articles_Status_PublishedAt",
                table: "Articles",
                columns: new[] { "Status", "PublishedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Articles_Status_PublishedAt",
                table: "Articles");
        }
    }
}
