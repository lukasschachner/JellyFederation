using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JellyFederation.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Servers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerUserId = table.Column<string>(type: "TEXT", nullable: false),
                    ApiKey = table.Column<string>(type: "TEXT", nullable: false),
                    RegisteredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsOnline = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Servers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestingServerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwningServerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    JellyfinItemId = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    SelectedTransportMode = table.Column<int>(type: "INTEGER", nullable: true),
                    TransportSelectionReason = table.Column<int>(type: "INTEGER", nullable: true),
                    FailureCategory = table.Column<int>(type: "INTEGER", nullable: true),
                    FailureReason = table.Column<string>(type: "TEXT", nullable: true),
                    BytesTransferred = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    TransferStartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileRequests_Servers_OwningServerId",
                        column: x => x.OwningServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FileRequests_Servers_RequestingServerId",
                        column: x => x.RequestingServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Invitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromServerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToServerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RespondedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invitations_Servers_FromServerId",
                        column: x => x.FromServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Invitations_Servers_ToServerId",
                        column: x => x.ToServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MediaItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ServerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    JellyfinItemId = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    Overview = table.Column<string>(type: "TEXT", nullable: true),
                    ImageUrl = table.Column<string>(type: "TEXT", nullable: true),
                    FileSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    IsRequestable = table.Column<bool>(type: "INTEGER", nullable: false),
                    IndexedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaItems_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileRequests_OwningServerId",
                table: "FileRequests",
                column: "OwningServerId");

            migrationBuilder.CreateIndex(
                name: "IX_FileRequests_RequestingServerId",
                table: "FileRequests",
                column: "RequestingServerId");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_FromServerId",
                table: "Invitations",
                column: "FromServerId");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_ToServerId",
                table: "Invitations",
                column: "ToServerId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_ServerId_Title",
                table: "MediaItems",
                columns: new[] { "ServerId", "Title" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_ServerId_Type",
                table: "MediaItems",
                columns: new[] { "ServerId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_Servers_ApiKey",
                table: "Servers",
                column: "ApiKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileRequests");

            migrationBuilder.DropTable(
                name: "Invitations");

            migrationBuilder.DropTable(
                name: "MediaItems");

            migrationBuilder.DropTable(
                name: "Servers");
        }
    }
}
