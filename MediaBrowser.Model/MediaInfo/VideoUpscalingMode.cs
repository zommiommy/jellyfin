namespace MediaBrowser.Model.MediaInfo;

/// <summary>
/// Video upscaling mode applied during transcoding.
/// </summary>
public enum VideoUpscalingMode
{
    /// <summary>
    /// Disable video upscaling.
    /// </summary>
    None = 0,

    /// <summary>
    /// Apply the configured FidelityFX Super Resolution shader.
    /// </summary>
    Fsr = 1,

    /// <summary>
    /// Apply the configured Anime4K Mode A shader chain.
    /// </summary>
    Anime4KModeA = 2,

    /// <summary>
    /// Apply the configured Anime4K Mode B shader chain.
    /// </summary>
    Anime4KModeB = 3,

    /// <summary>
    /// Apply the configured Anime4K Mode C shader chain.
    /// </summary>
    Anime4KModeC = 4,

    /// <summary>
    /// Apply the configured Anime4K Mode A+A shader chain.
    /// </summary>
    Anime4KModeAPlusA = 5,

    /// <summary>
    /// Apply the configured Anime4K Mode B+B shader chain.
    /// </summary>
    Anime4KModeBPlusB = 6,

    /// <summary>
    /// Apply the configured Anime4K Mode C+A shader chain.
    /// </summary>
    Anime4KModeCPlusA = 7
}
