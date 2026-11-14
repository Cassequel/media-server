using Microsoft.EntityFrameworkCore;
using Rockflix.API.Models;

namespace Rockflix.API.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Movie> Movies => Set<Movie>();
    public DbSet<TvShow> TvShows => Set<TvShow>();
    public DbSet<Episode> Episodes => Set<Episode>();
    public DbSet<WatchHistory> WatchHistory => Set<WatchHistory>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<TelegramUser> TelegramUsers => Set<TelegramUser>();
    public DbSet<TelegramRequest> TelegramRequests => Set<TelegramRequest>();
    public DbSet<MediaRequest> MediaRequests => Set<MediaRequest>();
    public DbSet<MovieVersion> MovieVersions => Set<MovieVersion>();
    public DbSet<MovieFilterJob> MovieFilterJobs => Set<MovieFilterJob>();
    public DbSet<FilterAssessment> FilterAssessments => Set<FilterAssessment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FilterAssessment>().HasKey(x => x.TmdbId);
        modelBuilder.Entity<MovieVersion>().HasOne(v => v.Movie).WithMany(m => m.Versions).HasForeignKey(v => v.MovieId);
        modelBuilder.Entity<MovieVersion>().HasIndex(v => v.FilePath).IsUnique();
        modelBuilder.Entity<MovieFilterJob>().HasOne(j => j.MovieVersion).WithMany().HasForeignKey(j => j.MovieVersionId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<MovieFilterJob>().HasIndex(j => new { j.TmdbId, j.RemoveSex, j.MuteLanguage })
            .IsUnique().HasFilter("\"Status\" NOT IN ('ready', 'failed', 'cancelled')");
        modelBuilder.Entity<MediaRequest>().HasOne(r => r.FilterJob).WithMany().HasForeignKey(r => r.FilterJobId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<WatchHistory>().HasOne(w => w.MovieVersion).WithMany().HasForeignKey(w => w.MovieVersionId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<WatchHistory>().HasIndex(w => new { w.UserId, w.MovieVersionId })
            .IsUnique().HasFilter("\"MovieVersionId\" IS NOT NULL");
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasIndex(u => u.TelegramChatId)
            .IsUnique()
            .HasFilter("\"TelegramChatId\" IS NOT NULL");

        // A user can have one watch history entry per movie (upsert on progress)
        modelBuilder.Entity<WatchHistory>()
            .HasIndex(w => new { w.UserId, w.MovieId })
            .IsUnique()
            .HasFilter("\"MovieId\" IS NOT NULL AND \"MovieVersionId\" IS NULL");

        modelBuilder.Entity<WatchHistory>()
            .HasIndex(w => new { w.UserId, w.EpisodeId })
            .IsUnique()
            .HasFilter("\"EpisodeId\" IS NOT NULL");

        modelBuilder.Entity<WatchHistory>()
            .HasOne(w => w.Movie)
            .WithMany(m => m.WatchHistory)
            .HasForeignKey(w => w.MovieId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WatchHistory>()
            .HasOne(w => w.Episode)
            .WithMany(e => e.WatchHistory)
            .HasForeignKey(w => w.EpisodeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TelegramRequest>()
            .HasOne(r => r.User)
            .WithMany(u => u.Requests)
            .HasForeignKey(r => r.ChatId)
            .HasPrincipalKey(u => u.ChatId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
