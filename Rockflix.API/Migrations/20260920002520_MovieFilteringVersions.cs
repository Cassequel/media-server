using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Rockflix.API.Migrations
{
    /// <inheritdoc />
    public partial class MovieFilteringVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WatchHistory_UserId_MovieId",
                table: "WatchHistory");

            migrationBuilder.AddColumn<int>(
                name: "MovieVersionId",
                table: "WatchHistory",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FilterJobId",
                table: "MediaRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReservedBytes",
                table: "MediaRequests",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "FilterAssessments",
                columns: table => new
                {
                    TmdbId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Json = table.Column<string>(type: "text", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilterAssessments", x => x.TmdbId);
                });

            migrationBuilder.CreateTable(
                name: "MovieVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MovieId = table.Column<int>(type: "integer", nullable: false),
                    RemoveSex = table.Column<bool>(type: "boolean", nullable: false),
                    MuteLanguage = table.Column<bool>(type: "boolean", nullable: false),
                    AudioLanguage = table.Column<string>(type: "text", nullable: false),
                    FilePath = table.Column<string>(type: "text", nullable: false),
                    DurationSeconds = table.Column<double>(type: "double precision", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    SourceFingerprint = table.Column<string>(type: "text", nullable: false),
                    Notice = table.Column<string>(type: "text", nullable: false),
                    EditPlanJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovieVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MovieVersions_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MovieFilterJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TmdbId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    RemoveSex = table.Column<bool>(type: "boolean", nullable: false),
                    MuteLanguage = table.Column<bool>(type: "boolean", nullable: false),
                    AcceptUnverified = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    PlanJson = table.Column<string>(type: "text", nullable: false),
                    Notice = table.Column<string>(type: "text", nullable: false),
                    RadarrMovieId = table.Column<int>(type: "integer", nullable: true),
                    RadarrCommandId = table.Column<int>(type: "integer", nullable: true),
                    OriginalRadarrPath = table.Column<string>(type: "text", nullable: false),
                    SourcePath = table.Column<string>(type: "text", nullable: false),
                    SourceFingerprint = table.Column<string>(type: "text", nullable: false),
                    DownloadId = table.Column<string>(type: "text", nullable: false),
                    ReservedBytes = table.Column<long>(type: "bigint", nullable: false),
                    MovieVersionId = table.Column<int>(type: "integer", nullable: true),
                    SourceCleaned = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovieFilterJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MovieFilterJobs_MovieVersions_MovieVersionId",
                        column: x => x.MovieVersionId,
                        principalTable: "MovieVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WatchHistory_MovieVersionId",
                table: "WatchHistory",
                column: "MovieVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_WatchHistory_UserId_MovieId",
                table: "WatchHistory",
                columns: new[] { "UserId", "MovieId" },
                unique: true,
                filter: "\"MovieId\" IS NOT NULL AND \"MovieVersionId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WatchHistory_UserId_MovieVersionId",
                table: "WatchHistory",
                columns: new[] { "UserId", "MovieVersionId" },
                unique: true,
                filter: "\"MovieVersionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MediaRequests_FilterJobId",
                table: "MediaRequests",
                column: "FilterJobId");

            migrationBuilder.CreateIndex(
                name: "IX_MovieFilterJobs_MovieVersionId",
                table: "MovieFilterJobs",
                column: "MovieVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_MovieFilterJobs_TmdbId_RemoveSex_MuteLanguage",
                table: "MovieFilterJobs",
                columns: new[] { "TmdbId", "RemoveSex", "MuteLanguage" },
                unique: true,
                filter: "\"Status\" NOT IN ('ready', 'failed', 'cancelled')");

            migrationBuilder.CreateIndex(
                name: "IX_MovieVersions_FilePath",
                table: "MovieVersions",
                column: "FilePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MovieVersions_MovieId",
                table: "MovieVersions",
                column: "MovieId");

            // Existing originals and their progress keep their identities; no media file is touched.
            migrationBuilder.Sql("""
                INSERT INTO "MovieVersions" ("MovieId", "RemoveSex", "MuteLanguage", "AudioLanguage", "FilePath", "DurationSeconds", "SizeBytes", "SourceFingerprint", "Notice", "EditPlanJson", "CreatedAt")
                SELECT "Id", false, false, 'unknown', "FilePath", COALESCE("RuntimeMinutes", 0) * 60, 0, '', '', '[]', "AddedAt"
                FROM "Movies" WHERE "FilePath" <> '';
                UPDATE "WatchHistory" w SET "MovieVersionId" = v."Id"
                FROM "MovieVersions" v WHERE w."MovieId" = v."MovieId";
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_MediaRequests_MovieFilterJobs_FilterJobId",
                table: "MediaRequests",
                column: "FilterJobId",
                principalTable: "MovieFilterJobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_WatchHistory_MovieVersions_MovieVersionId",
                table: "WatchHistory",
                column: "MovieVersionId",
                principalTable: "MovieVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MediaRequests_MovieFilterJobs_FilterJobId",
                table: "MediaRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_WatchHistory_MovieVersions_MovieVersionId",
                table: "WatchHistory");

            migrationBuilder.DropTable(
                name: "FilterAssessments");

            migrationBuilder.DropTable(
                name: "MovieFilterJobs");

            migrationBuilder.DropTable(
                name: "MovieVersions");

            migrationBuilder.DropIndex(
                name: "IX_WatchHistory_MovieVersionId",
                table: "WatchHistory");

            migrationBuilder.DropIndex(
                name: "IX_WatchHistory_UserId_MovieId",
                table: "WatchHistory");

            migrationBuilder.DropIndex(
                name: "IX_WatchHistory_UserId_MovieVersionId",
                table: "WatchHistory");

            migrationBuilder.DropIndex(
                name: "IX_MediaRequests_FilterJobId",
                table: "MediaRequests");

            migrationBuilder.DropColumn(
                name: "MovieVersionId",
                table: "WatchHistory");

            migrationBuilder.DropColumn(
                name: "FilterJobId",
                table: "MediaRequests");

            migrationBuilder.DropColumn(
                name: "ReservedBytes",
                table: "MediaRequests");

            migrationBuilder.CreateIndex(
                name: "IX_WatchHistory_UserId_MovieId",
                table: "WatchHistory",
                columns: new[] { "UserId", "MovieId" },
                unique: true,
                filter: "\"MovieId\" IS NOT NULL");
        }
    }
}
