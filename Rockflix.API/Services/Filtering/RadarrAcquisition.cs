using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Rockflix.API.Models;

namespace Rockflix.API.Services.Filtering;

public class RadarrAcquisition(IHttpClientFactory clients, IConfiguration config)
{
    private HttpClient Http => clients.CreateClient("radarr");
    public string SourceRoot => config["Filtering:AcquisitionPath"] ?? "/mnt/media/Movies/.rockflix-acquisition";
    public string JobRoot(MovieFilterJob job) => Path.Combine(SourceRoot, job.TmdbId.ToString(), job.Id.ToString("N"));
    private string RemoteRoot => config["Filtering:RadarrAcquisitionPath"] ?? "/movies/.rockflix-acquisition";

    // Persist returned movie ID/path before triggering search, so restarts reconcile rather than duplicate acquisitions.
    public async Task EnrollAsync(MovieFilterJob job, CancellationToken ct)
    {
        if (!Directory.Exists(SourceRoot)) throw new InvalidOperationException("The shared acquisition directory has not been prepared. Ask the administrator to complete filtering setup.");
        foreach (var folder in new[] { Path.Combine(SourceRoot, job.TmdbId.ToString()), JobRoot(job) })
        {
            Directory.CreateDirectory(folder);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.SetGroup);
        }
        var movies = await Http.GetFromJsonAsync<JsonArray>($"/api/v3/movie?tmdbId={job.TmdbId}", ct) ?? [];
        var movie = movies.OfType<JsonObject>().FirstOrDefault(m => m["tmdbId"]?.GetValue<int>() == job.TmdbId);
        string path = RemoteRoot.TrimEnd('/') + "/" + job.TmdbId + "/" + job.Id.ToString("N");
        if (movie == null)
        {
            // Addition uses the resolved TMDb identity and an explicit acquisition path.
            movie = new JsonObject { ["tmdbId"] = job.TmdbId, ["title"] = job.Title,
                ["qualityProfileId"] = config.GetValue<int?>("Filtering:QualityProfileId") ?? 4,
                ["rootFolderPath"] = config["Radarr:RootFolderPath"], ["path"] = path, ["monitored"] = false,
                ["minimumAvailability"] = "released", ["addOptions"] = new JsonObject { ["searchForMovie"] = false } };
            using var added = await Http.PostAsJsonAsync("/api/v3/movie", movie, ct);
            added.EnsureSuccessStatusCode();
            movie = await added.Content.ReadFromJsonAsync<JsonObject>(ct) ?? throw new InvalidOperationException("Radarr did not return a movie.");
        }
        else
        {
            job.OriginalRadarrPath = movie["path"]?.GetValue<string>() ?? "";
            movie["path"] = path;
            movie["monitored"] = false;
            using var updated = await Http.PutAsJsonAsync($"/api/v3/movie/{movie["id"]!.GetValue<int>()}?moveFiles=false", movie, ct);
            updated.EnsureSuccessStatusCode();
        }
        job.RadarrMovieId = movie["id"]!.GetValue<int>();
    }

    public async Task<int> SearchAsync(MovieFilterJob job, CancellationToken ct)
    {
        using var refresh = await Http.PostAsJsonAsync("/api/v3/command", new { name = "RefreshMovie", movieIds = new[] { job.RadarrMovieId!.Value } }, ct);
        refresh.EnsureSuccessStatusCode();
        var command = await refresh.Content.ReadFromJsonAsync<JsonObject>(ct);
        int id = command!["id"]!.GetValue<int>();
        // Await rescan so an old file record cannot satisfy the fresh search.
        for (int i = 0; i < 60; i++)
        {
            var state = await Http.GetFromJsonAsync<JsonObject>($"/api/v3/command/{id}", ct);
            string? status = state?["status"]?.GetValue<string>();
            if (status == "completed") break;
            if (status is "failed" or "aborted" || i == 59) throw new InvalidOperationException("Radarr rescan did not complete; acquisition stopped.");
            await Task.Delay(1000, ct);
        }
        using var search = await Http.PostAsJsonAsync("/api/v3/command", new { name = "MoviesSearch", movieIds = new[] { job.RadarrMovieId.Value } }, ct);
        search.EnsureSuccessStatusCode();
        var response = await search.Content.ReadFromJsonAsync<JsonObject>(ct);
        return response!["id"]!.GetValue<int>();
    }

    public async Task<string?> FindImportedAsync(MovieFilterJob job, CancellationToken ct)
    {
        if (job.RadarrMovieId == null) return null;
        var movie = await Http.GetFromJsonAsync<JsonObject>($"/api/v3/movie/{job.RadarrMovieId}", ct);
        string? path = movie?["movieFile"]?["path"]?.GetValue<string>();
        string prefix = RemoteRoot.TrimEnd('/') + "/" + job.TmdbId + "/" + job.Id.ToString("N") + "/";
        if (path == null || !path.StartsWith(prefix, StringComparison.Ordinal)) return null;
        string local = Path.Combine(JobRoot(job), path[prefix.Length..]);
        OwnedPath(local, JobRoot(job));
        return File.Exists(local) ? local : null;
    }

    public static string OwnedPath(string path, string root)
    {
        string full = Path.GetFullPath(path), parent = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        if (!full.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidOperationException("Media path is outside its assigned directory.");
        for (string? p = full; p != null; p = Path.GetDirectoryName(p))
        {
            if ((File.Exists(p) || Directory.Exists(p)) && File.GetAttributes(p).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException("Linked media paths are not supported for processing.");
            if (p == parent) break;
        }
        return full;
    }
}
