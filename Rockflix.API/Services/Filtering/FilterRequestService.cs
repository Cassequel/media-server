using Microsoft.EntityFrameworkCore;
using Rockflix.API.Data;
using Rockflix.API.Models;
using System.Text.Json;

namespace Rockflix.API.Services.Filtering;

public record MovieFilterOptions(bool RemoveSex = false, bool MuteLanguage = false, bool AcceptUnverified = false);
public record FilterRequestResult(string Message, int? MovieId = null, int? VersionId = null, Guid? JobId = null);

public class FilterRequestService(AppDbContext db, MovieTimestampService provider, IConfiguration config)
{
    public bool Enabled => config.GetValue<bool>("Filtering:Enabled");
    public async Task<FilterRequestResult> ConfirmAsync(int userId, int tmdbId, string title, MovieFilterOptions options, CancellationToken ct)
    {
        if (!Enabled) throw new InvalidOperationException("Movie filtering is not enabled yet.");
        if (tmdbId <= 0 || string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("A valid movie is required.");
        var user = await db.Users.FindAsync([userId], ct);
        if (user == null || !user.IsActive) throw new InvalidOperationException("An active account is required.");
        // External lookups occur before holding database locks.
        AssessmentResult? assessment = null;
        var existing = await FindVersion(tmdbId, options).FirstOrDefaultAsync(ct);
        if (existing != null && File.Exists(existing.FilePath)) return Ready(existing);
        if (options.RemoveSex || options.MuteLanguage)
        {
            assessment = await provider.AssessAsync(tmdbId, ct);
            if ((options.RemoveSex && assessment.Sex.Status == "unavailable") || (options.MuteLanguage && assessment.Language.Status == "unavailable"))
                throw new InvalidOperationException("A selected filter has no usable timestamps. Remove that option or choose original.");
            if (!options.AcceptUnverified) throw new InvalidOperationException("Please acknowledge the timestamp limitations before trying filtering.");
        }
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({tmdbId})", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({-(long)userId})", ct);
        existing = await FindVersion(tmdbId, options).FirstOrDefaultAsync(ct);
        if (existing != null && File.Exists(existing.FilePath)) return Ready(existing);
        var job = await db.MovieFilterJobs.FirstOrDefaultAsync(j => j.TmdbId == tmdbId && j.RemoveSex == options.RemoveSex && j.MuteLanguage == options.MuteLanguage
            && j.Status != "ready" && j.Status != "failed" && j.Status != "cancelled", ct);
        long reservation = 0;
        if (job == null)
        {
            reservation = config.GetValue<long?>("Filtering:DownloadReservationBytes") ?? 8L * 1024 * 1024 * 1024;
            long used = await db.MediaRequests.Where(r => r.UserId == userId).SumAsync(r => (r.FileSizeBytes ?? 0) + r.ReservedBytes, ct);
            if (!user.IsAdmin && used + reservation > 20L * 1024 * 1024 * 1024)
                throw new InvalidOperationException("Not enough download quota for this additional version.");
            var intervals = assessment?.Intervals.Where(i => i.Action == "skip" ? options.RemoveSex : options.MuteLanguage).ToList() ?? [];
            job = new MovieFilterJob { TmdbId = tmdbId, Title = title, RemoveSex = options.RemoveSex, MuteLanguage = options.MuteLanguage,
                AcceptUnverified = options.AcceptUnverified, ReservedBytes = reservation,
                PlanJson = JsonSerializer.Serialize(intervals), Notice = assessment == null ? "" : string.Join(" ",
                    new[] { options.RemoveSex ? assessment.Sex.Notice : "", options.MuteLanguage ? assessment.Language.Notice : "" }.Where(s => s.Length > 0)) };
            db.MovieFilterJobs.Add(job);
        }
        if (!await db.MediaRequests.AnyAsync(r => r.UserId == userId && r.FilterJobId == job.Id, ct))
            db.MediaRequests.Add(new MediaRequest { UserId = userId, RequestText = title, ResolvedTitle = title, MediaType = "movie", ExternalId = tmdbId,
                FilterJobId = job.Id, Status = "pending", ReservedBytes = reservation });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new("Your requested version is queued. Follow its progress in your requests.", JobId: job.Id);
    }
    private IQueryable<MovieVersion> FindVersion(int id, MovieFilterOptions o) => db.MovieVersions.Where(v => v.Movie.TmdbId == id
        && v.RemoveSex == o.RemoveSex && v.MuteLanguage == o.MuteLanguage).OrderByDescending(v => v.CreatedAt);
    private static FilterRequestResult Ready(MovieVersion v) => new("This exact version is already available.", v.MovieId, v.Id);
}
