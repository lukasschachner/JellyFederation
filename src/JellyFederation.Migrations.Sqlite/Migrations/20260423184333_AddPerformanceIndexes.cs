using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JellyFederation.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invitations_FromServerId",
                table: "Invitations");

            migrationBuilder.DropIndex(
                name: "IX_Invitations_ToServerId",
                table: "Invitations");

            migrationBuilder.DropIndex(
                name: "IX_FileRequests_OwningServerId",
                table: "FileRequests");

            migrationBuilder.DropIndex(
                name: "IX_FileRequests_RequestingServerId",
                table: "FileRequests");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_FromServerId_Status",
                table: "Invitations",
                columns: new[] { "FromServerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_ToServerId_Status",
                table: "Invitations",
                columns: new[] { "ToServerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FileRequests_OwningServerId_Status",
                table: "FileRequests",
                columns: new[] { "OwningServerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FileRequests_RequestingServerId_Status",
                table: "FileRequests",
                columns: new[] { "RequestingServerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FileRequests_Status_CreatedAt",
                table: "FileRequests",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invitations_FromServerId_Status",
                table: "Invitations");

            migrationBuilder.DropIndex(
                name: "IX_Invitations_ToServerId_Status",
                table: "Invitations");

            migrationBuilder.DropIndex(
                name: "IX_FileRequests_OwningServerId_Status",
                table: "FileRequests");

            migrationBuilder.DropIndex(
                name: "IX_FileRequests_RequestingServerId_Status",
                table: "FileRequests");

            migrationBuilder.DropIndex(
                name: "IX_FileRequests_Status_CreatedAt",
                table: "FileRequests");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_FromServerId",
                table: "Invitations",
                column: "FromServerId");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_ToServerId",
                table: "Invitations",
                column: "ToServerId");

            migrationBuilder.CreateIndex(
                name: "IX_FileRequests_OwningServerId",
                table: "FileRequests",
                column: "OwningServerId");

            migrationBuilder.CreateIndex(
                name: "IX_FileRequests_RequestingServerId",
                table: "FileRequests",
                column: "RequestingServerId");
        }
    }
}
