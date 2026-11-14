namespace Rockflix.API.Services.Filtering;

public class MovieTimestampService(DoesTheDogDieService dog, CleanStreamService community, ILogger<MovieTimestampService> logger)
{
    public async Task<AssessmentResult> AssessAsync(int tmdbId, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(115));
        AssessmentResult? additional = null;
        try { additional = await community.AssessAsync(tmdbId, deadline.Token); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or System.Text.Json.JsonException or OperationCanceledException && !ct.IsCancellationRequested)
        { logger.LogWarning("Community timestamp lookup unavailable for {TmdbId}; checking the existing provider", tmdbId); }
        AssessmentResult primary;
        using var providerDeadline = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        providerDeadline.CancelAfter(TimeSpan.FromSeconds(additional?.Sex.Status == "limited" ? 85 : 110));
        try { primary = await dog.AssessAsync(tmdbId, providerDeadline.Token); }
        catch (Exception ex) when (additional?.Sex.Status == "limited" && !ct.IsCancellationRequested &&
            ex is HttpRequestException or InvalidOperationException or System.Text.Json.JsonException or OperationCanceledException)
        { return additional with { Language = new("unavailable", 0, "The language timestamp source is unavailable. Choose sex/nudity only or retry.") }; }
        return Combine(primary, additional);
    }

    public static AssessmentResult Combine(AssessmentResult primary, AssessmentResult? community)
    {
        if (community?.Sex.Status != "limited") return primary;
        // Pick one source per category; copied VideoSkip data is not independent corroboration.
        return primary with { Sex = community.Sex, SourceUrl = community.SourceUrl,
            Intervals = community.Intervals.Where(i => i.Action == "skip").Concat(primary.Intervals.Where(i => i.Action == "mute")).ToList() };
    }
}
