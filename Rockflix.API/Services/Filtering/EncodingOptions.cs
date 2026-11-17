using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Rockflix.API.Services.Filtering;

/// <summary>Per-process FFmpeg thread tuning; container CPU quotas provide the hard ceiling.</summary>
public sealed class EncodingOptions
{
    public const string SectionName = "Encoding";

    [Range(1, int.MaxValue)]
    public int CodecThreads { get; set; } = 2;

    [Range(1, int.MaxValue)]
    public int FilterThreads { get; set; } = 1;

    public void Validate() => Validator.ValidateObject(this, new ValidationContext(this), validateAllProperties: true);

    // Input codec options must precede -i; output codec options must follow all inputs.
    public string[] InputArguments()
    {
        Validate();
        return ["-threads", CodecThreads.ToString(CultureInfo.InvariantCulture),
            "-filter_threads", FilterThreads.ToString(CultureInfo.InvariantCulture),
            "-filter_complex_threads", FilterThreads.ToString(CultureInfo.InvariantCulture)];
    }

    public string[] OutputArguments()
    {
        Validate();
        return ["-threads", CodecThreads.ToString(CultureInfo.InvariantCulture)];
    }
}
