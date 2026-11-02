namespace DiscImageStudio.Burning;

/// <summary>
/// Stage identifiers carried by <see cref="OpticalBurnProgress.Stage"/>. The shell keys its
/// progress display on these exact strings, so every backend must emit the same values.
/// Only <see cref="Write"/> and <see cref="Complete"/> advance the device progress bar.
/// </summary>
public static class OpticalBurnStages
{
    public const string Device = "device";
    public const string Speed = "speed";
    public const string Transfer = "transfer";
    public const string Buffer = "buffer";
    public const string Media = "media";
    public const string Write = "write";
    public const string Finalize = "finalize";
    public const string Diagnostic = "diagnostic";
    public const string Complete = "complete";
}
