using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JellyFederation.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddMediaItemIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MediaItems_ServerId",
                table: "MediaItems");

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_ServerId_Title",
                table: "MediaItems",
                columns: new[] { "ServerId", "Title" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_ServerId_Type",
                table: "MediaItems",
                columns: new[] { "ServerId", "Type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MediaItems_ServerId_Title",
                table: "MediaItems");

            migrationBuilder.DropIndex(
                name: "IX_MediaItems_ServerId_Type",
                table: "MediaItems");

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_ServerId",
                table: "MediaItems",
                column: "ServerId");
        }
    }
}
