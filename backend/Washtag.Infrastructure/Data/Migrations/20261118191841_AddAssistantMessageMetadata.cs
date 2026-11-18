using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Washtag.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAssistantMessageMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AiPhrased",
                table: "AssistantMessages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Intent",
                table: "AssistantMessages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "AssistantMessages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuggestionsJson",
                table: "AssistantMessages",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiPhrased",
                table: "AssistantMessages");

            migrationBuilder.DropColumn(
                name: "Intent",
                table: "AssistantMessages");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "AssistantMessages");

            migrationBuilder.DropColumn(
                name: "SuggestionsJson",
                table: "AssistantMessages");
        }
    }
}
