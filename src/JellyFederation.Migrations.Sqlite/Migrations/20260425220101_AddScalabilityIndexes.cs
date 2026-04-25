using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JellyFederation.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddScalabilityIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Servers_RegisteredAt_Id",
                table: "Servers",
                columns: new[] { "RegisteredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_ServerId_IndexedAt",
                table: "MediaItems",
                columns: new[] { "ServerId", "IndexedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_ServerId_JellyfinItemId",
                table: "MediaItems",
                columns: new[] { "ServerId", "JellyfinItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_FromServerId_CreatedAt_Id",
                table: "Invitations",
                columns: new[] { "FromServerId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_ToServerId_CreatedAt_Id",
                table: "Invitations",
                columns: new[] { "ToServerId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FileRequests_OwningServerId_CreatedAt_Id",
                table: "FileRequests",
                columns: new[] { "OwningServerId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FileRequests_RequestingServerId_CreatedAt_Id",
                table: "FileRequests",
                columns: new[] { "RequestingServerId", "CreatedAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Servers_RegisteredAt_Id",
                table: "Servers");

            migrationBuilder.DropIndex(
                name: "IX_MediaItems_ServerId_IndexedAt",
                table: "MediaItems");

            migrationBuilder.DropIndex(
                name: "IX_MediaItems_ServerId_JellyfinItemId",
                table: "MediaItems");

            migrationBuilder.DropIndex(
                name: "IX_Invitations_FromServerId_CreatedAt_Id",
                table: "Invitations");

            migrationBuilder.DropIndex(
                name: "IX_Invitations_ToServerId_CreatedAt_Id",
                table: "Invitations");

            migrationBuilder.DropIndex(
                name: "IX_FileRequests_OwningServerId_CreatedAt_Id",
                table: "FileRequests");

            migrationBuilder.DropIndex(
                name: "IX_FileRequests_RequestingServerId_CreatedAt_Id",
                table: "FileRequests");
        }
    }
}
