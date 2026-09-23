using System;
using System.Collections.Generic;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Api.Models.MediaInfoDtos;

/// <summary>
/// Playback info dto.
/// </summary>
public class PlaybackInfoDto
{
    /// <summary>
    /// Gets or sets the playback userId.
    /// </summary>
    public Guid? UserId { get; set; }

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
    /// Gets or sets the media source id.
    /// </summary>
    public string? MediaSourceId { get; set; }

    /// <summary>
    /// Gets or sets the live stream id.
    /// </summary>
    public string? LiveStreamId { get; set; }

    /// <summary>
    /// Gets or sets the device profile.
    /// </summary>
    public DeviceProfile? DeviceProfile { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to enable direct play.
    /// </summary>
    public bool? EnableDirectPlay { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to enable direct stream.
    /// </summary>
    public bool? EnableDirectStream { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to enable transcoding.
    /// </summary>
    public bool? EnableTranscoding { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to enable video stream copy.
    /// </summary>
    public bool? AllowVideoStreamCopy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to allow audio stream copy.
    /// </summary>
    public bool? AllowAudioStreamCopy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to auto open the live stream.
    /// </summary>
    public bool? AutoOpenLiveStream { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether always burn in subtitles when transcoding.
    /// </summary>
    public bool? AlwaysBurnInSubtitleWhenTranscoding { get; set; }

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

    /// <summary>
    /// Gets or sets a value indicating whether to convert SDR video to HDR using NVIDIA RTX Video HDR.
    /// </summary>
    public bool VideoTrueHdr { get; set; }

    /// <summary>
    /// Gets or sets the TrueHDR peak brightness in nits, from 400 to 2000. Defaults to 1000.
    /// </summary>
    public int? VideoTrueHdrPeakBrightness { get; set; }

    /// <summary>
    /// Gets or sets the TrueHDR contrast, from 0 to 200. Defaults to 100.
    /// </summary>
    public int? VideoTrueHdrContrast { get; set; }

    /// <summary>
    /// Gets or sets the TrueHDR saturation, from 0 to 200. Defaults to 100.
    /// </summary>
    public int? VideoTrueHdrSaturation { get; set; }

    /// <summary>
    /// Gets or sets the TrueHDR middle gray, from 10 to 100. Defaults to 50.
    /// </summary>
    public int? VideoTrueHdrMiddleGray { get; set; }
}
