namespace Rockflix.API.Models;

public class MovieVersion
{
    public int Id { get; set; }
    public int MovieId { get; set; }
    public Movie Movie { get; set; } = null!;
    public bool RemoveSex { get; set; }
    public bool MuteLanguage { get; set; }
    public string AudioLanguage { get; set; } = "unknown";
    public string FilePath { get; set; } = "";
    public double DurationSeconds { get; set; }
    public long SizeBytes { get; set; }
    public string SourceFingerprint { get; set; } = "";
    public string Notice { get; set; } = "";
    public string EditPlanJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Label => RemoveSex ? (MuteLanguage ? "Sex/nudity + language filtered" : "Sex/nudity filtered")
        : MuteLanguage ? "Language filtered" : "Original";
}

public class MovieFilterJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int TmdbId { get; set; }
    public string Title { get; set; } = "";
    public bool RemoveSex { get; set; }
    public bool MuteLanguage { get; set; }
    public bool AcceptUnverified { get; set; }
    public string Status { get; set; } = "queued";
    public string Message { get; set; } = "Waiting for download";
    public string PlanJson { get; set; } = "[]";
    public string Notice { get; set; } = "";
    public int? RadarrMovieId { get; set; }
    public int? RadarrCommandId { get; set; }
    public string OriginalRadarrPath { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string SourceFingerprint { get; set; } = "";
    public string DownloadId { get; set; } = "";
    public long ReservedBytes { get; set; }
    public int? MovieVersionId { get; set; }
    public MovieVersion? MovieVersion { get; set; }
    public bool SourceCleaned { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class FilterAssessment
{
    public int TmdbId { get; set; }
    public string Json { get; set; } = "";
    public DateTime FetchedAt { get; set; }
}
