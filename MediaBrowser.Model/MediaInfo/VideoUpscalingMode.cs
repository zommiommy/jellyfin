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
    Anime4KModeA = 2
}
