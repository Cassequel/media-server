using System.Text.Json;
using Rockflix.API.Services.Filtering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Rockflix.API.Data;
using Rockflix.API.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Rockflix.API.Services;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Rockflix.API.Controllers;

int passed = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); passed++; }
await ResourceRegressionTests.RunAsync(Check, args.Contains("--media"));
void Reject(Action action, string label) { try { action(); } catch (InvalidOperationException) { Check(true, label); return; } throw new Exception("FAIL: " + label); }
var edits = new List<EditInterval> { new(2, 4, "skip"), new(3, 5, "skip"), new(7, 8, "mute") };
var merged = EditPlan.Merge(edits, "skip");
Check(merged.Count == 1 && merged[0].Start == 2 && merged[0].End == 5, "overlapping cuts merged");
var keep = EditPlan.KeepRanges(10, edits);
Check(keep.Count == 2 && keep.Sum(k => k.End - k.Start) == 7, "kept timeline duration");
Reject(() => EditPlan.KeepRanges(10, [new(1, 12, "skip")]), "out-of-bounds cut refused");
Reject(() => EditPlan.KeepRanges(10, [new(0, 10, "skip")]), "whole-movie cut refused");
Reject(() => EditPlan.Merge([new(double.NaN, 10, "mute")], "mute"), "non-finite timestamps refused");
Reject(() => RadarrAcquisition.OwnedPath("/tmp/outside/movie.mp4", "/tmp/owned"), "path traversal refused");
string srt = "1\n00:00:01,000 --> 00:00:06,000\nhello fuck damn\n\n2\n00:00:07,000 --> 00:00:08,000\nworld\n";
string transformed = SubtitleEdits.Apply(srt, keep, true);
Check(!transformed.Contains("fuck") && transformed.Contains("damn"), "strong subtitle language masked; mild word preserved");
Check(transformed.Contains("00:00:04,000 --> 00:00:05,000"), "subtitles retimed across cuts");
using (var doc = JsonDocument.Parse("""
    [{"topicId":279,"yes":1,"position1":0,"position2":0,"position3":2,"safePosition1":0,"safePosition2":0,"safePosition3":5},
     {"topicId":251,"yes":1,"position1":0,"position2":0,"position3":7,"safePosition1":0,"safePosition2":0,"safePosition3":8},
     {"topicId":279,"yes":1,"position1":null},
     {"topicId":279,"yes":1,"position1":0,"position2":0,"position3":4,"safePosition1":0,"safePosition2":0,"safePosition3":1},
     {"topicId":999,"yes":1,"position1":0,"position2":0,"position3":1,"safePosition1":0,"safePosition2":0,"safePosition3":2}]
    """))
{
    var parsed = DoesTheDogDieService.ParseRatings(doc.RootElement);
    Check(parsed.Count == 2 && parsed[0].Action == "skip" && parsed[1].Action == "mute", "provider mapping rejects missing, inverted and unrelated topics");
}

var queried = new List<string>();
var recovered = await DoesTheDogDieService.ReadIntervalsAsync(123, path =>
{
    queried.Add(path);
    string json = !path.Contains('?') ? "[" + string.Join(',',Enumerable.Repeat("{}",200)) + "]"
        : path.EndsWith("topicId=279") ? """[{"topicId":279,"yes":1,"position1":0,"position2":0,"position3":2,"safePosition1":0,"safePosition2":0,"safePosition3":5}]""" : "[]";
    return Task.FromResult(JsonDocument.Parse(json));
});
Check(recovered.Count == 1 && queried.Count == 8 && queried.Skip(1).All(p => p.Contains("topicId=")), "capped all-topic results fall back to all seven relevant topics");
queried.Clear();
await DoesTheDogDieService.ReadIntervalsAsync(123, path => { queried.Add(path); return Task.FromResult(JsonDocument.Parse("[]")); });
Check(queried.Count == 1, "uncapped rating lookup avoids unnecessary topic requests");

using (var communityJson = JsonDocument.Parse("""
    {"imdbId":"tt15398776","type":"movie","segments":[
      {"id":"a","startMs":1000,"endMs":2500,"category":"nudity","severity":"high","verified":true},
      {"id":"duplicate","startMs":1000,"endMs":2500,"category":"nudity"},
      {"startMs":3000,"endMs":4000,"category":"sex","severity":"low"},
      {"startMs":5000,"endMs":6000,"category":"violence","severity":"high"},
      {"startMs":7000,"endMs":8000,"category":"language","severity":"high"},
      {"startMs":9000,"endMs":8000,"category":"nudity"},
      {"startMs":-1,"endMs":1000,"category":"nudity"},
      {"startMs":1000,"endMs":99999999,"category":"nudity"}]}
    """))
{
    var communityEdits = CleanStreamService.Parse(communityJson.RootElement,"tt15398776");
    Check(communityEdits.Count == 1 && communityEdits[0].Start == 1 && communityEdits[0].End == 2.5,
        "community parser converts milliseconds, deduplicates, rejects unsafe times and preserves romance/violence/language");
    Check(!communityEdits[0].Professional && communityEdits[0].SourceName == "CleanStream / VideoSkip" && communityEdits[0].SourceSegmentId == "a",
        "community provenance retained without promoting verification claims");
    Reject(() => CleanStreamService.Parse(communityJson.RootElement,"tt0000000"), "wrong community movie identity refused");
    var primary = new AssessmentResult(872585,new("limited",1,"primary"),new("limited",1,"language"),edits,"https://www.doesthedogdie.com",DateTime.UtcNow);
    var secondary = new AssessmentResult(872585,new("limited",1,"community"),new("unavailable",0,"none"),communityEdits,"https://cleanstream.elfhosted.com",DateTime.UtcNow);
    var combined = MovieTimestampService.Combine(primary,secondary);
    Check(combined.Intervals.Count == 2 && combined.Intervals.Single(i => i.Action == "skip").Start == 1 && combined.Language == primary.Language,
        "community sex source selected once while independent language option is retained");
    Check(MovieTimestampService.Combine(primary,null) == primary, "unavailable community source preserves existing provider");
}
using (var disabledCommunity = new CleanStreamService(new FixedClientFactory(new HttpClient(new NoNetworkHandler())),new ConfigurationBuilder().Build()))
    Check(await disabledCommunity.AssessAsync(872585,default) == null, "community integration is opt-in and avoids network outside pilot allowlist");

string work = Path.Combine(Path.GetTempPath(), "rockflix-filter-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(work);
string source = Path.Combine(work, "source.mp4");
var processor = new MediaProcessor();
if (args.Contains("--media"))
{
    await MediaProcessor.RunAsync("ffmpeg", ["-nostdin", "-v", "error", "-f", "lavfi", "-i", "color=blue:size=160x90:rate=25:duration=10,drawbox=color=red:t=fill:enable='gte(t,2)*lt(t,5)'", "-f", "lavfi", "-i", "sine=frequency=1000:sample_rate=48000:duration=10", "-c:v", "libx264", "-c:a", "aac", "-metadata:s:a:0", "language=eng", source], default);
    var fingerprint = await MediaProcessor.FingerprintAsync(source, default);
    foreach (var scenario in new[] { "sex", "language", "both" })
    {
        var plan = edits.Where(e => scenario == "both" || (scenario == "sex" ? e.Action == "skip" : e.Action == "mute")).ToList();
        string output = Path.Combine(work, scenario + ".mp4");
        var result = await processor.ProcessAsync(source, output, plan, scenario != "sex", default);
        Check(Math.Abs(result.Duration - (scenario == "language" ? 10 : 7)) < .15, scenario + " output duration and decode");
        Check(fingerprint == await MediaProcessor.FingerprintAsync(source, default), scenario + " preserves original during processing");
        string pixels = Path.Combine(work, scenario + ".rgb");
        await MediaProcessor.RunAsync("ffmpeg", ["-v", "error", "-i", output, "-vf", "scale=1:1", "-pix_fmt", "rgb24", "-f", "rawvideo", pixels], default);
        byte[] rgb = await File.ReadAllBytesAsync(pixels);
        bool hasRed = Enumerable.Range(0, rgb.Length / 3).Any(i => rgb[i * 3] > rgb[i * 3 + 2] + 100);
        Check(scenario == "language" ? hasRed : !hasRed, scenario + " keeps/removes marked video frames independently of audio");
        if (scenario != "sex")
        {
            // The mute at source second 7 must land at output second 4 after the 3-second cut.
            string quiet = await MediaProcessor.RunAsync("ffmpeg", ["-v", "info", "-ss", scenario == "both" ? "4.2" : "7.2", "-t", "0.5", "-i", output, "-af", "astats=metadata=1:reset=0,ametadata=print:key=lavfi.astats.Overall.RMS_level:file=-", "-f", "null", "-"], default);
            Check(quiet.Contains("=-inf"), scenario + " mute remains aligned after cuts");
        }
    }
    string badSource = Path.Combine(work, "unknown-language.mp4");
    await MediaProcessor.RunAsync("ffmpeg", ["-v", "error", "-i", source, "-c", "copy", "-metadata:s:a:0", "language=und", badSource], default);
    try { await processor.ProcessAsync(badSource, Path.Combine(work, "bad.mp4"), [new(1,2,"mute")], true, default); throw new Exception("Wrong language accepted"); }
    catch (InvalidOperationException) { Check(true, "unknown audio language fails without publishing"); }
}

if (Environment.GetEnvironmentVariable("ROCKFLIX_TEST_CONNECTION") is { } connection)
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
        ["Filtering:Enabled"]="true", ["Filtering:DownloadReservationBytes"]="1024", ["DoesTheDogDie:ApiKey"]="test-only",
        ["Filtering:LibraryPath"]="/src/artifacts/worker-media/library", ["Filtering:WorkPath"]="/src/artifacts/worker-media/work",
        ["Filtering:AcquisitionPath"]="/src/artifacts/worker-media/acquisition", ["Media:RootPath"]="/src", ["Filtering:FreeSpaceFloorBytes"]="0"
    }).Build());
    services.AddDbContext<AppDbContext>(b => b.UseNpgsql(connection));
    services.AddSingleton<DoesTheDogDieService>();
    services.AddSingleton<CleanStreamService>();
    services.AddSingleton<MovieTimestampService>();
    services.AddScoped<FilterRequestService>();
    services.AddScoped<RadarrAcquisition>();
    services.AddSingleton<MediaProcessor>();
    services.AddHttpClient<TmdbService>();
    services.AddHttpClient("doesthedogdie").ConfigurePrimaryHttpMessageHandler(() => new NoNetworkHandler());
    await using var sp = services.BuildServiceProvider();
    using var scope = sp.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // Dedicated test database only. It is initialized from the production schema (no user data).
    var oldMovie = new Movie { Title="Fixture", TmdbId=987654, FilePath=source };
    await db.Database.ExecuteSqlRawAsync("""
        INSERT INTO "Users" ("Username","Email","PasswordHash","IsAdmin","IsActive","CreatedAt") VALUES ('fixture','fixture@example.invalid','not-a-password',false,true,NOW());
        INSERT INTO "Movies" ("Title","TmdbId","FilePath","AddedAt") VALUES ('Fixture',987654,'/tmp/fixture-original.mp4',NOW());
        INSERT INTO "WatchHistory" ("UserId","MovieId","ProgressSeconds","Completed","LastWatched") SELECT u."Id",m."Id",12,false,NOW() FROM "Users" u CROSS JOIN "Movies" m LIMIT 1;
        """);
    await db.Database.MigrateAsync();
    Check(await db.MovieVersions.CountAsync() == 1, "migration backfills original version");
    Check(await db.WatchHistory.AllAsync(w => w.MovieVersionId != null && w.ProgressSeconds == 12), "migration preserves watch progress");
    var original = await db.MovieVersions.SingleAsync();
    original.FilePath = source;
    if (!File.Exists(source)) await File.WriteAllTextAsync(source,"fixture");
    await db.SaveChangesAsync();
    int user = await db.Users.Select(u => u.Id).FirstAsync();
    var requests = scope.ServiceProvider.GetRequiredService<FilterRequestService>();
    var reuse = await requests.ConfirmAsync(user, 987654, "Fixture", new(), default);
    Check(reuse.VersionId == original.Id && !await db.MediaRequests.AnyAsync(), "original reuse has no download or charge");
    var assessment = new AssessmentResult(987654,new("limited",1,"test"),new("limited",1,"test"),edits,"https://www.doesthedogdie.com",DateTime.UtcNow);
    db.FilterAssessments.Add(new FilterAssessment { TmdbId=987654, FetchedAt=DateTime.UtcNow, Json=JsonSerializer.Serialize(assessment,new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
    await db.SaveChangesAsync();
    try { await requests.ConfirmAsync(user,987654,"Fixture",new(true,false,false),default); throw new Exception("No acknowledgement accepted"); }
    catch (InvalidOperationException) { Check(true,"best-effort acknowledgement required server-side"); }
    var first = await requests.ConfirmAsync(user,987654,"Fixture",new(true,false,true),default);
    using var secondScope = sp.CreateScope();
    var second = await secondScope.ServiceProvider.GetRequiredService<FilterRequestService>().ConfirmAsync(user,987654,"Fixture",new(true,false,true),default);
    Check(first.JobId == second.JobId && await db.MovieFilterJobs.CountAsync() == 1, "repeat request shares the exact job");
    Check(await db.MediaRequests.CountAsync() == 1, "repeat request does not reserve quota twice");
    var other = await requests.ConfirmAsync(user,987654,"Fixture",new(false,true,true),default);
    Check(other.JobId != first.JobId && await db.MovieFilterJobs.CountAsync() == 2, "different filter combination has separate acquisition");
    Check(await db.MovieVersions.CountAsync() == 1, "request does not alter existing original");
    if (args.Contains("--media"))
    {
        var job = await db.MovieFilterJobs.SingleAsync(j => j.Id == first.JobId);
        var later = await db.MovieFilterJobs.SingleAsync(j => j.Id == other.JobId);
        later.Status = "needs_decision";
        var acquisition = scope.ServiceProvider.GetRequiredService<RadarrAcquisition>();
        Directory.CreateDirectory(acquisition.JobRoot(job));
        job.SourcePath = Path.Combine(acquisition.JobRoot(job),"source.mp4");
        File.Copy(source,job.SourcePath);
        job.Status = "checking";
        await db.SaveChangesAsync();
        var worker = new MovieFilterWorker(sp.GetRequiredService<IServiceScopeFactory>(),sp.GetRequiredService<IConfiguration>(),sp.GetRequiredService<ILogger<MovieFilterWorker>>());
        await worker.TickAsync(default);
        await db.Entry(job).ReloadAsync();
        Check(job.Status == "ready" && job.MovieVersionId != null, "worker validates and publishes the requested version");
        var published = await db.MovieVersions.SingleAsync(v => v.Id == job.MovieVersionId);
        Check(File.Exists(published.FilePath) && File.Exists(job.SourcePath), "publication commits before source cleanup");
        byte[] publishedBytes = await File.ReadAllBytesAsync(published.FilePath);
        File.Delete(published.FilePath);
        await worker.TickAsync(default);
        Check(File.Exists(job.SourcePath), "missing published output prevents deleting the only source");
        await File.WriteAllBytesAsync(published.FilePath,publishedBytes);
        await worker.TickAsync(default);
        Check(!File.Exists(job.SourcePath) && File.Exists(published.FilePath), "successful cleanup retains only the new requested output");
        await worker.TickAsync(default);
        Check(await db.MovieVersions.CountAsync() == 2, "repeated worker iteration does not publish duplicate versions");
        Check(File.Exists(original.FilePath), "another explicitly stored original survives filtered publication");
        Directory.CreateDirectory(acquisition.JobRoot(later));
        later.SourcePath = Path.Combine(acquisition.JobRoot(later),"invalid.mp4");
        await File.WriteAllTextAsync(later.SourcePath,"not media");
        later.Status = "checking";
        await db.SaveChangesAsync();
        await worker.TickAsync(default);
        await db.Entry(later).ReloadAsync();
        Check(later.Status == "needs_decision" && File.Exists(later.SourcePath), "failed media validation retains source and requests a decision");
        Check(await db.MovieVersions.CountAsync() == 2, "failed processing never publishes an original fallback");
    }
    async Task<FilterRequestResult> ConcurrentRequest()
    {
        using var s = sp.CreateScope();
        return await s.ServiceProvider.GetRequiredService<FilterRequestService>().ConfirmAsync(user, 777777, "Concurrent fixture", new(), default);
    }
    var concurrent = await Task.WhenAll(ConcurrentRequest(),ConcurrentRequest());
    Check(concurrent[0].JobId == concurrent[1].JobId, "simultaneous requests share a durable job");
    Check(await db.MediaRequests.CountAsync(r => r.ExternalId == 777777) == 1, "simultaneous confirmations reserve quota once");
    var undecided = new MovieFilterJob { TmdbId=888888,Title="Unpublished fixture",RemoveSex=true,Status="needs_decision",SourcePath=source,ReservedBytes=1024 };
    db.MovieFilterJobs.Add(undecided);
    db.MediaRequests.Add(new MediaRequest { UserId=user,MediaType="movie",FilterJob=undecided,ReservedBytes=1024 });
    await db.SaveChangesAsync();
    int jobsBefore = await db.MovieFilterJobs.CountAsync();
    var controller = new MovieFilteringController(db,sp.GetRequiredService<MovieTimestampService>(),requests)
    { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,user.ToString())],"test")) } } };
    var switched = await controller.ChooseOriginal(undecided.Id,default);
    Check(switched is OkObjectResult && !undecided.RemoveSex && undecided.Status == "checking", "explicit original choice changes the unpublished output only");
    Check(await db.MovieFilterJobs.CountAsync() == jobsBefore && File.Exists(source), "original fallback reuses staged input without a second acquisition or output");
}
if (args.Contains("--http"))
{
    var token = new JwtSecurityToken("test", "test", [new Claim(ClaimTypes.NameIdentifier,"1")], expires:DateTime.UtcNow.AddMinutes(5),
        signingCredentials:new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-only-signing-key-abcdefghijklmnopqrstuvwxyz")),SecurityAlgorithms.HmacSha256));
    var client = new HttpClient { BaseAddress=new Uri("http://rockflix-filter-test-api:5244") };
    client.DefaultRequestHeaders.Authorization = new("Bearer",new JwtSecurityTokenHandler().WriteToken(token));
    var movies = await client.GetFromJsonAsync<JsonArray>("/api/movies");
    Check(movies!.Count == 1, "authenticated library endpoint returns one title with versions");
    int movieId = movies[0]!["id"]!.GetValue<int>();
    var movie = await client.GetFromJsonAsync<JsonObject>($"/api/movies/{movieId}");
    int versionId = movie!["versions"]!.AsArray().Single(v => v!["removeSex"]!.GetValue<bool>())!["id"]!.GetValue<int>();
    var progress = await client.PostAsJsonAsync($"/api/movies/{movieId}/progress",new { versionId,progressSeconds=3,completed=false });
    Check(progress.IsSuccessStatusCode, "progress endpoint accepts the exact version");
    var history = await client.GetFromJsonAsync<JsonArray>("/api/watchhistory/continue-watching");
    Check(history!.Any(w => w?["movieVersionId"]?.GetValue<int>() == versionId), "continue watching retains version identity");
    using var stream = new HttpRequestMessage(HttpMethod.Get,$"/api/stream/movie/{movieId}?versionId={versionId}");
    stream.Headers.Range = new(0,99);
    var playback = await client.SendAsync(stream);
    Check(playback.StatusCode == HttpStatusCode.PartialContent && (await playback.Content.ReadAsByteArrayAsync()).Length == 100, "filtered playback supports HTTP range seeking");
    var wrong = await client.GetAsync($"/api/stream/movie/{movieId}?versionId=999999");
    Check(wrong.StatusCode == HttpStatusCode.NotFound, "unknown version never falls back to original");
    var capabilities = await client.GetFromJsonAsync<JsonObject>("/api/movie-filtering/capabilities");
    Check(capabilities!["enabled"]!.GetValue<bool>() == false, "feature flag defaults to disabled for rollout");
    Check((await client.GetAsync("/api/movie-filtering/jobs")).IsSuccessStatusCode, "request progress endpoint serializes stored jobs");
}
if (args.Contains("--radarr"))
{
    string key = XDocument.Load("/src/artifacts/radarr-config/config.xml").Root!.Element("ApiKey")!.Value;
    var http = new HttpClient { BaseAddress = new Uri("http://rockflix-filter-test-radarr:7878") };
    http.DefaultRequestHeaders.Add("X-Api-Key", key);
    var folders = await http.GetFromJsonAsync<JsonArray>("/api/v3/rootfolder");
    if (folders!.Count == 0) (await http.PostAsJsonAsync("/api/v3/rootfolder", new { path = "/movies" })).EnsureSuccessStatusCode();
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
        ["Filtering:AcquisitionPath"]="/movies/.rockflix-acquisition", ["Filtering:RadarrAcquisitionPath"]="/movies/.rockflix-acquisition",
        ["Radarr:RootFolderPath"]="/movies", ["Filtering:QualityProfileId"]="4"
    }).Build();
    var client = new RadarrAcquisition(new FixedClientFactory(http), config);
    Directory.CreateDirectory(client.SourceRoot);
    var first = new MovieFilterJob { TmdbId = 578, Title = "Jaws" };
    await client.EnrollAsync(first, default);
    Check(first.RadarrMovieId > 0, "real Radarr accepts new acquisition with explicit path");
    string firstFile = Path.Combine(client.JobRoot(first), "original-fixture.txt");
    await File.WriteAllTextAsync(firstFile,"existing asset must survive reacquisition");
    int command = await client.SearchAsync(first, default);
    Check(command > 0, "real Radarr rescan/search command accepted (no download clients or indexers configured)");
    var second = new MovieFilterJob { TmdbId=578, Title="Jaws", RemoveSex=true };
    await client.EnrollAsync(second,default);
    Check(first.RadarrMovieId == second.RadarrMovieId, "reacquisition reuses movie identity without duplicate Radarr entry");
    Check(File.Exists(firstFile), "Radarr path reassignment preserves previous version bytes");
    Check(client.JobRoot(first) != client.JobRoot(second), "acquisition paths isolate each request");
    Check(await client.FindImportedAsync(second,default) == null, "previous file cannot satisfy new import");
}
Console.WriteLine($"All {passed} checks passed. Test artifacts: {work}");

sealed class NoNetworkHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
        throw new InvalidOperationException("Unexpected external network call in test.");
}
sealed class FixedClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}
