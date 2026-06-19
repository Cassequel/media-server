using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Rockflix.API.Data;
using Rockflix.API.Models;

namespace Rockflix.API.Services;

public partial class MediaScannerService(AppDbContext db, TmdbService tmdb, IConfiguration config, ILogger<MediaScannerService> logger)
{
    private readonly string _mediaRoot = config["Media:RootPath"]!;

    public async Task ScanAsync()
    {
        await ScanMoviesAsync();
        await ScanTvShowsAsync();
        await PruneDeletedAsync();
    }

    private async Task PruneDeletedAsync()
    {
        // Mark pending movie requests as completed if the movie is already in the library
        var pendingMovies = await db.MediaRequests
            .Where(r => r.Status == "pending" && r.MediaType == "movie" && r.ResolvedTitle != null)
            .ToListAsync();
        foreach (var req in pendingMovies)
        {
            var exists = await db.Movies.AnyAsync(m => EF.Functions.ILike(m.Title, req.ResolvedTitle!));
            if (exists) req.Status = "completed";
        }

        // Mark pending TV requests as completed if the show is already in the library
        var pendingTv = await db.MediaRequests
            .Where(r => r.Status == "pending" && r.MediaType == "tv" && r.ResolvedTitle != null)
            .ToListAsync();
        foreach (var req in pendingTv)
        {
            var exists = await db.TvShows.AnyAsync(s => EF.Functions.ILike(s.Title, req.ResolvedTitle!));
            if (exists) req.Status = "completed";
        }

        await db.SaveChangesAsync();

        // Remove movies whose file no longer exists
        var movies = await db.Movies.ToListAsync();
        var deletedMovies = movies.Where(m => !File.Exists(m.FilePath)).ToList();
        if (deletedMovies.Any())
        {
            db.Movies.RemoveRange(deletedMovies);
            foreach (var m in deletedMovies)
            {
                logger.LogInformation("Removed missing movie: {Title}", m.Title);
                var req = await db.MediaRequests
                    .Where(r => r.MediaType == "movie" && r.ResolvedTitle != null &&
                                EF.Functions.ILike(r.ResolvedTitle, m.Title) && r.Status == "completed")
                    .FirstOrDefaultAsync();
                if (req != null) { req.FileSizeBytes = 0; req.Status = "removed"; }
            }
        }

        // Remove episodes whose file no longer exists
        var episodes = await db.Episodes.Include(e => e.TvShow).ToListAsync();
        var deletedEpisodes = episodes.Where(e => !File.Exists(e.FilePath)).ToList();
        if (deletedEpisodes.Any())
        {
            db.Episodes.RemoveRange(deletedEpisodes);
            foreach (var e in deletedEpisodes)
                logger.LogInformation("Removed missing episode: {Show} S{S}E{E}", e.TvShow.Title, e.SeasonNumber, e.EpisodeNumber);
        }

        await db.SaveChangesAsync();

        // Remove TV shows with no remaining episodes and clear their storage
        var emptyShows = await db.TvShows
            .Where(s => !db.Episodes.Any(e => e.TvShowId == s.Id))
            .ToListAsync();
        if (emptyShows.Any())
        {
            db.TvShows.RemoveRange(emptyShows);
            foreach (var s in emptyShows)
            {
                logger.LogInformation("Removed empty show: {Title}", s.Title);
                var req = await db.MediaRequests
                    .Where(r => r.MediaType == "tv" && r.ResolvedTitle != null &&
                                EF.Functions.ILike(r.ResolvedTitle, s.Title) && r.Status == "completed")
                    .FirstOrDefaultAsync();
                if (req != null) { req.FileSizeBytes = 0; req.Status = "removed"; }
            }
            await db.SaveChangesAsync();
        }
    }

    private async Task ScanMoviesAsync()
    {
        var moviesPath = Path.Combine(_mediaRoot, "Movies");
        if (!Directory.Exists(moviesPath)) return;

        foreach (var file in Directory.EnumerateFiles(moviesPath, "*", SearchOption.AllDirectories))
        {
            if (!IsVideoFile(file)) continue;
            if (await db.Movies.AnyAsync(m => m.FilePath == file)) continue;

            var (title, year) = ParseMovieFilename(Path.GetFileNameWithoutExtension(file));
            var meta = await tmdb.SearchMovieAsync(title, year);

            var movie = new Movie
            {
                Title = meta?.Title ?? title,
                Year = year,
                FilePath = file,
                Description = meta?.Description,
                PosterPath = meta?.PosterPath,
                BackdropPath = meta?.BackdropPath,
                TmdbRating = meta?.Rating,
                TmdbId = meta?.TmdbId
            };

            db.Movies.Add(movie);
            logger.LogInformation("Added movie: {Title}", movie.Title);
        }

        await db.SaveChangesAsync();
    }

    private async Task ScanTvShowsAsync()
    {
        var tvPath = Path.Combine(_mediaRoot, "TV Shows");
        if (!Directory.Exists(tvPath)) return;

        foreach (var showDir in Directory.GetDirectories(tvPath))
        {
            var showName = Path.GetFileName(showDir);
            var show = await db.TvShows.Include(s => s.Episodes)
                .FirstOrDefaultAsync(s => s.Title == showName);

            if (show == null)
            {
                var meta = await tmdb.SearchTvShowAsync(showName, null);
                show = new TvShow
                {
                    Title = meta?.Title ?? showName,
                    Description = meta?.Description,
                    PosterPath = meta?.PosterPath,
                    BackdropPath = meta?.BackdropPath,
                    TmdbRating = meta?.Rating,
                    TmdbId = meta?.TmdbId
                };
                db.TvShows.Add(show);
                await db.SaveChangesAsync();
                logger.LogInformation("Added show: {Title}", show.Title);
            }

            foreach (var file in Directory.EnumerateFiles(showDir, "*", SearchOption.AllDirectories))
            {
                if (!IsVideoFile(file)) continue;
                if (await db.Episodes.AnyAsync(e => e.FilePath == file)) continue;

                var (season, episode) = ParseEpisodeFilename(Path.GetFileNameWithoutExtension(file));
                if (season == 0 || episode == 0) continue;

                TmdbEpisodeResult? epMeta = null;
                if (show.TmdbId.HasValue)
                    epMeta = await tmdb.GetEpisodeAsync(show.TmdbId.Value, season, episode);

                db.Episodes.Add(new Episode
                {
                    TvShowId = show.Id,
                    SeasonNumber = season,
                    EpisodeNumber = episode,
                    Title = epMeta?.Title ?? $"S{season:D2}E{episode:D2}",
                    Description = epMeta?.Description,
                    StillPath = epMeta?.StillPath,
                    RuntimeMinutes = epMeta?.RuntimeMinutes,
                    FilePath = file
                });
            }

            await db.SaveChangesAsync();
        }
    }

    private static (string title, int? year) ParseMovieFilename(string name)
    {
        var match = MovieTitleRegex().Match(name);
        if (match.Success)
            return (match.Groups[1].Value.Trim(), int.Parse(match.Groups[2].Value));
        return (name, null);
    }

    private static (int season, int episode) ParseEpisodeFilename(string name)
    {
        var match = EpisodeRegex().Match(name);
        if (match.Success)
            return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
        return (0, 0);
    }

    private static bool IsVideoFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".mp4" or ".mkv" or ".avi" or ".mov" or ".m4v";
    }

    [GeneratedRegex(@"^(.+?)\s\((\d{4})\)")]
    private static partial Regex MovieTitleRegex();

    [GeneratedRegex(@"[Ss](\d{1,2})[Ee](\d{1,2})")]
    private static partial Regex EpisodeRegex();
}
