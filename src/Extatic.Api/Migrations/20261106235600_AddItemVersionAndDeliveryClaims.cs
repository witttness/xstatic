using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Extatic.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddItemVersionAndDeliveryClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClaimedUntil",
                table: "webhook_delivery_logs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "items",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateIndex(
                name: "IX_webhook_delivery_logs_SucceededAt_NextRetryAt_ClaimedUntil",
                table: "webhook_delivery_logs",
                columns: new[] { "SucceededAt", "NextRetryAt", "ClaimedUntil" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_webhook_delivery_logs_SucceededAt_NextRetryAt_ClaimedUntil",
                table: "webhook_delivery_logs");

            migrationBuilder.DropColumn(
                name: "ClaimedUntil",
                table: "webhook_delivery_logs");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "items");
        }
    }
}
