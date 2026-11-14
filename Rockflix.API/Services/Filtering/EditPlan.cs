using System.Globalization;

namespace Rockflix.API.Services.Filtering;

public record EditInterval(double Start, double End, string Action, int TopicId = 0, bool Professional = false,
    string SourceName = "DoesTheDogDie", string SourceUrl = "https://www.doesthedogdie.com", string SourceSegmentId = "");
public record FilterCategory(string Status, int IntervalCount, string Notice);
public record AssessmentResult(int TmdbId, FilterCategory Sex, FilterCategory Language, List<EditInterval> Intervals,
    string SourceUrl, DateTime FetchedAt);

public static class EditPlan
{
    public const string UnverifiedNotice = "Available timestamps are unverified for this copy. Filtering may miss content or remove the wrong moments.";
    public static List<EditInterval> Merge(IEnumerable<EditInterval> intervals, string action)
    {
        var sorted = intervals.Where(i => i.Action == action).OrderBy(i => i.Start).ToList();
        var result = new List<EditInterval>();
        foreach (var i in sorted)
        {
            if (!double.IsFinite(i.Start) || !double.IsFinite(i.End) || i.Start < 0 || i.End <= i.Start)
                throw new InvalidOperationException("Invalid edit interval.");
            if (result.Count > 0 && i.Start <= result[^1].End)
                result[^1] = result[^1] with { End = Math.Max(i.End, result[^1].End) };
            else result.Add(i);
        }
        return result;
    }

    public static List<EditInterval> KeepRanges(double duration, IEnumerable<EditInterval> intervals)
    {
        if (!double.IsFinite(duration) || duration <= 0) throw new InvalidOperationException("Invalid movie duration.");
        var result = new List<EditInterval>();
        double cursor = 0;
        foreach (var cut in Merge(intervals, "skip"))
        {
            if (cut.End > duration) throw new InvalidOperationException("Scene timestamps extend beyond this movie edition.");
            if (cut.Start > cursor) result.Add(new(cursor, cut.Start, "keep"));
            cursor = cut.End;
        }
        if (cursor < duration) result.Add(new(cursor, duration, "keep"));
        if (result.Count == 0) throw new InvalidOperationException("The edit plan removes the entire movie.");
        return result;
    }

    public static string Number(double n) => n.ToString("0.######", CultureInfo.InvariantCulture);

    public static string AudioFilter(IEnumerable<EditInterval> edits)
    {
        var mutes = Merge(edits, "mute");
        return mutes.Count == 0 ? "anull" : "volume=0:enable='" +
            // Small guard bands account for audio frame boundaries and codec delay.
            string.Join("+", mutes.Select(m => $"between(t,{Number(Math.Max(0, m.Start - .05))},{Number(m.End + .05)})")) + "'";
    }
}
