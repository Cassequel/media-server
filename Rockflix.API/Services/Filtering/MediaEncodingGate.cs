namespace Rockflix.API.Services.Filtering;

// Playback conversions and filtering share one encoder in this API process.
public static class MediaEncodingGate
{
    public static SemaphoreSlim Semaphore { get; } = new(1, 1);
}
