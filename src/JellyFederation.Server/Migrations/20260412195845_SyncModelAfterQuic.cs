using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JellyFederation.Server.Migrations
{
    /// <inheritdoc />
    public partial class SyncModelAfterQuic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BytesTransferred",
                table: "FileRequests",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "FailureCategory",
                table: "FileRequests",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SelectedTransportMode",
                table: "FileRequests",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TotalBytes",
                table: "FileRequests",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TransferStartedAt",
                table: "FileRequests",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TransportSelectionReason",
                table: "FileRequests",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BytesTransferred",
                table: "FileRequests");

            migrationBuilder.DropColumn(
                name: "FailureCategory",
                table: "FileRequests");

            migrationBuilder.DropColumn(
                name: "SelectedTransportMode",
                table: "FileRequests");

            migrationBuilder.DropColumn(
                name: "TotalBytes",
                table: "FileRequests");

            migrationBuilder.DropColumn(
                name: "TransferStartedAt",
                table: "FileRequests");

            migrationBuilder.DropColumn(
                name: "TransportSelectionReason",
                table: "FileRequests");
        }
    }
}
