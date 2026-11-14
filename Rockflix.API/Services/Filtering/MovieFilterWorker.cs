using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rockflix.API.Data;
using Rockflix.API.Models;

namespace Rockflix.API.Services.Filtering;

public class MovieFilterWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<MovieFilterWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue<bool>("Filtering:Enabled")) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Movie filtering worker iteration failed"); }
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    public async Task TickAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var acquisition = scope.ServiceProvider.GetRequiredService<RadarrAcquisition>();
        var processor = scope.ServiceProvider.GetRequiredService<MediaProcessor>();
        // Session lock spans external work but not a database transaction; only one encoder/acquirer runs across API replicas.
        await db.Database.OpenConnectionAsync(ct);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(730092041)";
        if (!(bool)(await command.ExecuteScalarAsync(ct))!) return;
        try
        {
            var jobs = await db.MovieFilterJobs.Where(j => (j.Status != "ready" && j.Status != "cancelled" && j.Status != "failed") || !j.SourceCleaned)
                .OrderBy(j => j.CreatedAt).ToListAsync(ct);
            foreach (var job in jobs)
            {
                if (job.Status == "needs_decision")
                {
                    if (job.UpdatedAt < DateTime.UtcNow.AddDays(-7))
                    {
                        job.Status = "cancelled"; job.Message = "Request expired after seven days awaiting a decision.";
                        foreach (var r in await db.MediaRequests.Where(r => r.FilterJobId == job.Id && r.Status == "pending").ToListAsync(ct))
                        { r.Status = "cancelled"; r.ReservedBytes = 0; }
                        await db.SaveChangesAsync(ct);
                    }
                    else continue;
                }
                // Different combinations of one title cannot share Radarr's acquisition slot.
                if (jobs.Any(j => j.TmdbId == job.TmdbId && j.CreatedAt < job.CreatedAt && j.Status != "ready" && j.Status != "cancelled" && j.Status != "failed")) continue;
                try { await AdvanceAsync(db, acquisition, processor, job, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Movie job {JobId} stopped", job.Id);
                    if (job.Status is "ready" or "cancelled" or "failed") { job.Message = "Source cleanup will retry. The published version, if any, is unchanged."; }
                    else
                    {
                        job.Status = "needs_decision";
                        job.Message = "Processing stopped: " + (ex is InvalidOperationException ? ex.Message : "The download or processing service failed. Retry or choose original.");
                    }
                    job.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
            }
        }
        finally
        {
            command.CommandText = "SELECT pg_advisory_unlock(730092041)";
            await command.ExecuteScalarAsync(CancellationToken.None);
        }
    }

    private async Task AdvanceAsync(AppDbContext db, RadarrAcquisition acquisition, MediaProcessor processor, MovieFilterJob job, CancellationToken ct)
    {
        var root = config["Filtering:LibraryPath"] ?? "/mnt/media/Rockflix/Movies";
        var workRoot = config["Filtering:WorkPath"] ?? "/mnt/media/Rockflix/.processing";
        var mediaRoot = config["Media:RootPath"] ?? "/mnt/media";
        if (!DriveInfo.GetDrives().Any(d => d.Name.TrimEnd('/') == mediaRoot.TrimEnd('/')))
            throw new InvalidOperationException("The media disk is not mounted; processing will not use the boot disk.");
        Directory.CreateDirectory(root); Directory.CreateDirectory(workRoot);
        if (job.Status is "cancelled" or "failed")
        {
            if (job.UpdatedAt > DateTime.UtcNow.AddHours(-24)) return;
            if (File.Exists(job.SourcePath)) File.Delete(RadarrAcquisition.OwnedPath(job.SourcePath, acquisition.JobRoot(job)));
            string abandoned = Path.Combine(workRoot, job.Id.ToString("N"));
            if (Directory.Exists(abandoned))
            {
                foreach (string f in Directory.EnumerateFiles(abandoned)) File.Delete(RadarrAcquisition.OwnedPath(f, abandoned));
                Directory.Delete(abandoned);
            }
            string finalDir = Path.Combine(root, job.TmdbId.ToString());
            if (Directory.Exists(finalDir))
                foreach (string file in Directory.EnumerateFiles(finalDir).Where(f => Path.GetFileNameWithoutExtension(f) == job.Id.ToString("N")))
                    if (!await db.MovieVersions.AnyAsync(v => v.FilePath == file, ct)) File.Delete(RadarrAcquisition.OwnedPath(file, finalDir));
            job.SourceCleaned = true;
            await db.SaveChangesAsync(ct);
            return;
        }
        if (job.Status == "ready")
        {
            if (File.Exists(job.SourcePath))
            {
                var published = await db.MovieVersions.FirstOrDefaultAsync(v => v.Id == job.MovieVersionId, ct);
                if (published == null || !File.Exists(published.FilePath) || new FileInfo(published.FilePath).Length != published.SizeBytes)
                    throw new InvalidOperationException("Published output is missing or changed; the source has been retained.");
                RadarrAcquisition.OwnedPath(job.SourcePath, acquisition.JobRoot(job));
                if (await MediaProcessor.FingerprintAsync(job.SourcePath, ct) != job.SourceFingerprint)
                    throw new InvalidOperationException("Source changed since publication; automatic cleanup refused.");
                File.Delete(job.SourcePath);
            }
            job.SourceCleaned = true;
            job.Message = "Ready. Download-client source retention follows existing seeding and cleanup settings.";
            await db.SaveChangesAsync(ct);
            return;
        }
        if (job.Status is "queued" or "waiting_space" or "enrolling")
        {
            if (!await HasSpaceAsync(db, root, job.ReservedBytes * 2, job.Id, ct))
            { await State(db, job, "waiting_space", "Waiting for enough disk space to download and process.", ct); return; }
            await State(db, job, "enrolling", "Preparing movie download", ct);
            if (job.RadarrMovieId == null) { await acquisition.EnrollAsync(job, ct); await db.SaveChangesAsync(ct); }
            // Persist before side effect. A crash during search goes to reconciliation, never blindly triggers a second search.
            await State(db, job, "searching", "Starting download search", ct);
            job.RadarrCommandId = await acquisition.SearchAsync(job, ct);
            await State(db, job, "downloading", "Downloading requested version", ct);
            return;
        }
        if (job.Status is "downloading" or "searching")
        {
            string? source = await acquisition.FindImportedAsync(job, ct);
            if (source == null)
            {
                if (job.Status == "searching" || job.UpdatedAt < DateTime.UtcNow.AddDays(-2))
                    await State(db, job, "needs_decision", "Download status needs attention. Check the download client before retrying acquisition.", ct);
                return;
            }
            job.SourcePath = source;
            await State(db, job, "checking", "Checking downloaded media", ct);
        }
        if (job.Status is not ("checking" or "filtering" or "validating" or "waiting_processing_space")) return;
        RadarrAcquisition.OwnedPath(job.SourcePath, acquisition.JobRoot(job));
        if (!File.Exists(job.SourcePath)) throw new InvalidOperationException("The downloaded source is missing.");
        long sourceBytes = new FileInfo(job.SourcePath).Length;
        if (!await HasSpaceAsync(db, root, Math.Max(sourceBytes * 3, job.ReservedBytes), job.Id, ct))
        { await State(db, job, "waiting_processing_space", "Waiting for space to process this file", ct); return; }
        var request = await db.MediaRequests.Include(r => r.User).FirstAsync(r => r.FilterJobId == job.Id && r.ReservedBytes > 0, ct);
        long others = await db.MediaRequests.Where(r => r.UserId == request.UserId && r.Id != request.Id).SumAsync(r => (r.FileSizeBytes ?? 0) + r.ReservedBytes, ct);
        if (!request.User.IsAdmin && others + sourceBytes > 20L * 1024 * 1024 * 1024)
            throw new InvalidOperationException("The downloaded file exceeds your remaining quota. Cancel this request or free quota before retrying.");
        string work = Path.Combine(workRoot, job.Id.ToString("N"));
        Directory.CreateDirectory(work);
        string temporary = Path.Combine(work, "output.mp4");
        var edits = JsonSerializer.Deserialize<List<EditInterval>>(job.PlanJson) ?? [];
        var sourceProbe = await processor.ProbeAsync(job.SourcePath, ct);
        job.SourceFingerprint = await MediaProcessor.FingerprintAsync(job.SourcePath, ct);
        ProcessedMedia output;
        if (job.RemoveSex || job.MuteLanguage)
        {
            if ((job.RemoveSex && !edits.Any(i => i.Action == "skip")) || (job.MuteLanguage && !edits.Any(i => i.Action == "mute")))
                throw new InvalidOperationException("A requested filter has no usable edits.");
            await State(db, job, "filtering", "Applying selected filters", ct);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromHours(config.GetValue<double?>("Filtering:EncodeTimeoutHours") ?? 6));
            output = await processor.ProcessAsync(job.SourcePath, temporary, edits, job.MuteLanguage, deadline.Token);
        }
        else
        {
            // Original output is moved on the same media filesystem; do not transcode or duplicate it.
            output = new(sourceProbe.Duration, sourceBytes, job.SourceFingerprint, "");
        }
        await State(db, job, "validating", "Validating and publishing requested version", ct);
        if (!request.User.IsAdmin && others + output.Size > 20L * 1024 * 1024 * 1024)
            throw new InvalidOperationException("The output exceeds your remaining quota; it was not published.");
        var movie = await db.Movies.FirstOrDefaultAsync(m => m.TmdbId == job.TmdbId, ct);
        if (movie == null)
        {
            movie = new Movie { Title = job.Title, TmdbId = job.TmdbId };
            try
            {
                using var metadataScope = scopes.CreateScope();
                var metadata = await metadataScope.ServiceProvider.GetRequiredService<TmdbService>().SearchMovieAsync(job.Title, null);
                if (metadata?.TmdbId == job.TmdbId)
                { movie.Description = metadata.Description; movie.PosterPath = metadata.PosterPath; movie.BackdropPath = metadata.BackdropPath; movie.TmdbRating = metadata.Rating; }
            }
            catch (Exception ex) { logger.LogWarning(ex, "Metadata refresh deferred for {TmdbId}", job.TmdbId); }
            db.Movies.Add(movie);
        }
        string dir = Path.Combine(root, job.TmdbId.ToString());
        Directory.CreateDirectory(dir);
        string final = Path.Combine(dir, job.Id.ToString("N") + (job.RemoveSex || job.MuteLanguage ? ".mp4" : Path.GetExtension(job.SourcePath)));
        // Copying is crash-safe: source stays until the committed version can be recovered. Cleanup removes it afterwards.
        if (job.RemoveSex || job.MuteLanguage) File.Move(temporary, final, overwrite: true);
        else
        {
            await processor.ValidateAsync(job.SourcePath, sourceProbe.Duration, ct);
            File.Copy(job.SourcePath, temporary, overwrite: true);
            File.Move(temporary, final, overwrite: true);
        }
        if (movie.FilePath.Length == 0) { movie.FilePath = final; movie.RuntimeMinutes = (int)Math.Ceiling(output.Duration / 60); }
        var version = new MovieVersion { Movie = movie, RemoveSex = job.RemoveSex, MuteLanguage = job.MuteLanguage,
            AudioLanguage = sourceProbe.AudioLanguage, FilePath = final, DurationSeconds = output.Duration, SizeBytes = output.Size,
            SourceFingerprint = output.Fingerprint, EditPlanJson = job.PlanJson, Notice = string.Join(" ", new[] { job.Notice, output.Notice }.Where(s => s.Length > 0)) };
        db.MovieVersions.Add(version);
        await using var publish = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({-(long)request.UserId})", ct);
        others = await db.MediaRequests.Where(r => r.UserId == request.UserId && r.Id != request.Id).SumAsync(r => (r.FileSizeBytes ?? 0) + r.ReservedBytes, ct);
        if (!request.User.IsAdmin && others + output.Size > 20L * 1024 * 1024 * 1024)
        {
            db.Entry(version).State = EntityState.Detached;
            if (db.Entry(movie).State == EntityState.Added) db.Entry(movie).State = EntityState.Detached;
            throw new InvalidOperationException("Quota changed while processing. Free quota before retrying publication.");
        }
        job.MovieVersion = version;
        job.Status = "ready"; job.Message = "Ready"; job.UpdatedAt = DateTime.UtcNow;
        foreach (var r in await db.MediaRequests.Where(r => r.FilterJobId == job.Id).ToListAsync(ct))
        { if (r.ReservedBytes > 0) r.FileSizeBytes = output.Size; r.ReservedBytes = 0; r.Status = "completed"; }
        await db.SaveChangesAsync(ct);
        await publish.CommitAsync(ct);
        // Only job-owned intermediate files; never traverse arbitrary directories or remove a movie version.
        foreach (var f in Directory.EnumerateFiles(work)) File.Delete(RadarrAcquisition.OwnedPath(f, work));
        Directory.Delete(work);
    }

    private async Task<bool> HasSpaceAsync(AppDbContext db, string root, long need, Guid jobId, CancellationToken ct)
    {
        long reserved = await db.MovieFilterJobs.Where(j => j.Id != jobId && j.Status != "ready" && j.Status != "cancelled" && j.Status != "failed")
            .SumAsync(j => j.ReservedBytes, ct);
        long floor = config.GetValue<long?>("Filtering:FreeSpaceFloorBytes") ?? 10L * 1024 * 1024 * 1024;
        return new DriveInfo(root).AvailableFreeSpace >= need + reserved + floor;
    }
    private static async Task State(AppDbContext db, MovieFilterJob j, string status, string message, CancellationToken ct)
    { j.Status = status; j.Message = message; j.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(ct); }
}
