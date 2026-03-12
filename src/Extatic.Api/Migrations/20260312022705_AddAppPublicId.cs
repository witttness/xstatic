using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Extatic.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAppPublicId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "apps",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "");

            // Backfill existing rows with unique public IDs before adding the unique index.
            // Uses md5(random() || id) — no extension required.
            migrationBuilder.Sql(@"
                UPDATE apps
                SET ""PublicId"" = 'app_' || substring(md5(random()::text || ""Id""::text), 1, 16)
                WHERE ""PublicId"" = '';
            ");

            migrationBuilder.CreateIndex(
                name: "IX_apps_PublicId",
                table: "apps",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_apps_PublicId",
                table: "apps");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "apps");
        }
    }
}
