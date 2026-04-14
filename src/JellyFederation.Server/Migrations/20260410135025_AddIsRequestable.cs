using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JellyFederation.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddIsRequestable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRequestable",
                table: "MediaItems",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsRequestable",
                table: "MediaItems");
        }
    }
}
