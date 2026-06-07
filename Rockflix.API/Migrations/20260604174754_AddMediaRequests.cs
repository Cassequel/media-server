using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Rockflix.API.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Favorites_Movies_MovieId",
                table: "Favorites");

            migrationBuilder.DropForeignKey(
                name: "FK_Favorites_TvShows_TvShowId",
                table: "Favorites");

            migrationBuilder.DropIndex(
                name: "IX_TelegramUsers_ChatId",
                table: "TelegramUsers");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "TelegramChatId",
                table: "Users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "TelegramUsers",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);

            migrationBuilder.CreateTable(
                name: "MediaRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    RequestText = table.Column<string>(type: "text", nullable: false),
                    MediaType = table.Column<string>(type: "text", nullable: false),
                    ResolvedTitle = table.Column<string>(type: "text", nullable: true),
                    ExternalId = table.Column<int>(type: "integer", nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaRequests_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_TelegramChatId",
                table: "Users",
                column: "TelegramChatId",
                unique: true,
                filter: "\"TelegramChatId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MediaRequests_UserId",
                table: "MediaRequests",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Favorites_Movies_MovieId",
                table: "Favorites",
                column: "MovieId",
                principalTable: "Movies",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Favorites_TvShows_TvShowId",
                table: "Favorites",
                column: "TvShowId",
                principalTable: "TvShows",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Favorites_Movies_MovieId",
                table: "Favorites");

            migrationBuilder.DropForeignKey(
                name: "FK_Favorites_TvShows_TvShowId",
                table: "Favorites");

            migrationBuilder.DropTable(
                name: "MediaRequests");

            migrationBuilder.DropIndex(
                name: "IX_Users_TelegramChatId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TelegramChatId",
                table: "Users");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "TelegramUsers",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.CreateIndex(
                name: "IX_TelegramUsers_ChatId",
                table: "TelegramUsers",
                column: "ChatId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Favorites_Movies_MovieId",
                table: "Favorites",
                column: "MovieId",
                principalTable: "Movies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Favorites_TvShows_TvShowId",
                table: "Favorites",
                column: "TvShowId",
                principalTable: "TvShows",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
