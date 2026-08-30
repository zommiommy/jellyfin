using System;
using System.Collections.Generic;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Api.Models.MediaInfoDtos;

/// <summary>
/// Open live stream dto.
/// </summary>
public class OpenLiveStreamDto
{
    /// <summary>
    /// Gets or sets the open token.
    /// </summary>
    public string? OpenToken { get; set; }

    /// <summary>
    /// Gets or sets the user id.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Gets or sets the play session id.
    /// </summary>
    public string? PlaySessionId { get; set; }

    /// <summary>
    /// Gets or sets the max streaming bitrate.
    /// </summary>
    public int? MaxStreamingBitrate { get; set; }

    /// <summary>
    /// Gets or sets the start time in ticks.
    /// </summary>
    public long? StartTimeTicks { get; set; }

    /// <summary>
    /// Gets or sets the audio stream index.
    /// </summary>
    public int? AudioStreamIndex { get; set; }

    /// <summary>
    /// Gets or sets the subtitle stream index.
    /// </summary>
    public int? SubtitleStreamIndex { get; set; }

    /// <summary>
    /// Gets or sets the max audio channels.
    /// </summary>
    public int? MaxAudioChannels { get; set; }

    /// <summary>
    /// Gets or sets the item id.
    /// </summary>
    public Guid? ItemId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to enable direct play.
    /// </summary>
    public bool? EnableDirectPlay { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to enable direct stream.
    /// </summary>
    public bool? EnableDirectStream { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether always burn in subtitles when transcoding.
    /// </summary>
    public bool? AlwaysBurnInSubtitleWhenTranscoding { get; set; }

    /// <summary>
    /// Gets or sets the device profile.
    /// </summary>
    public DeviceProfile? DeviceProfile { get; set; }

    /// <summary>
    /// Gets or sets the device play protocols.
    /// </summary>
    public IReadOnlyList<MediaProtocol> DirectPlayProtocols { get; set; } = Array.Empty<MediaProtocol>();

    /// <summary>
    /// Gets or sets the requested video upscaling mode.
    /// </summary>
    public VideoUpscalingMode VideoUpscalingMode { get; set; }

    /// <summary>
    /// Gets or sets the maximum video upscaling output width.
    /// </summary>
    public int? VideoUpscalingWidth { get; set; }

    /// <summary>
    /// Gets or sets the maximum video upscaling output height.
    /// </summary>
    public int? VideoUpscalingHeight { get; set; }

    /// <summary>
    /// Gets or sets advanced server-side video upscaling options.
    /// </summary>
    public IReadOnlyDictionary<string, string>? VideoUpscalingOptions { get; set; }
}
