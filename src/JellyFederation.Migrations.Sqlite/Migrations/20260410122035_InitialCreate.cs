using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JellyFederation.Migrations.Sqlite
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
                    Id = table.Column<Guid>(nullable: false),
                    Name = table.Column<string>(nullable: false),
                    OwnerUserId = table.Column<string>(nullable: false),
                    ApiKey = table.Column<string>(nullable: false),
                    RegisteredAt = table.Column<DateTime>(nullable: false),
                    LastSeenAt = table.Column<DateTime>(nullable: false),
                    IsOnline = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Servers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    RequestingServerId = table.Column<Guid>(nullable: false),
                    OwningServerId = table.Column<Guid>(nullable: false),
                    JellyfinItemId = table.Column<string>(nullable: false),
                    Status = table.Column<int>(nullable: false),
                    FailureReason = table.Column<string>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CompletedAt = table.Column<DateTime>(nullable: true)
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
                    Id = table.Column<Guid>(nullable: false),
                    FromServerId = table.Column<Guid>(nullable: false),
                    ToServerId = table.Column<Guid>(nullable: false),
                    Status = table.Column<int>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    RespondedAt = table.Column<DateTime>(nullable: true)
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
                    Id = table.Column<Guid>(nullable: false),
                    ServerId = table.Column<Guid>(nullable: false),
                    JellyfinItemId = table.Column<string>(nullable: false),
                    Title = table.Column<string>(nullable: false),
                    Type = table.Column<int>(nullable: false),
                    Year = table.Column<int>(nullable: true),
                    Overview = table.Column<string>(nullable: true),
                    ImageUrl = table.Column<string>(nullable: true),
                    FileSizeBytes = table.Column<long>(nullable: false),
                    IndexedAt = table.Column<DateTime>(nullable: false)
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
                name: "IX_MediaItems_ServerId",
                table: "MediaItems",
                column: "ServerId");

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
