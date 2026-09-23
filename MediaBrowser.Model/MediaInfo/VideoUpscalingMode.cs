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
    Anime4KModeCPlusA = 7,

    /// <summary>
    /// Apply NVIDIA RTX Video Super Resolution at low quality.
    /// </summary>
    NvidiaVsrLow = 8,

    /// <summary>
    /// Apply NVIDIA RTX Video Super Resolution at medium quality.
    /// </summary>
    NvidiaVsrMedium = 9,

    /// <summary>
    /// Apply NVIDIA RTX Video Super Resolution at high quality.
    /// </summary>
    NvidiaVsrHigh = 10,

    /// <summary>
    /// Apply NVIDIA RTX Video Super Resolution at ultra quality.
    /// </summary>
    NvidiaVsrUltra = 11,

    /// <summary>
    /// Apply NVIDIA RTX Video Super Resolution Q at low quality.
    /// </summary>
    NvidiaVsrQLow = 12,

    /// <summary>
    /// Apply NVIDIA RTX Video Super Resolution Q at medium quality.
    /// </summary>
    NvidiaVsrQMedium = 13,

    /// <summary>
    /// Apply NVIDIA RTX Video Super Resolution Q at high quality.
    /// </summary>
    NvidiaVsrQHigh = 14,

    /// <summary>
    /// Apply NVIDIA RTX Video Super Resolution Q at ultra quality.
    /// </summary>
    NvidiaVsrQUltra = 15
}
