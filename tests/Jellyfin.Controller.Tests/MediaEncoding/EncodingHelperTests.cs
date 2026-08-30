using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Moq;
using Xunit;

using IConfiguration = Microsoft.Extensions.Configuration.IConfiguration;

namespace Jellyfin.Controller.Tests.MediaEncoding;

public class EncodingHelperTests
{
    [Fact]
    public void GetMapArgs_NoSubtitle_ExcludesAllSubs()
    {
        var state = BuildState(subtitle: null, deliveryMethod: null);
        var args = CreateHelper().GetMapArgs(state);

        Assert.Contains("-map -0:s", args, StringComparison.Ordinal);
        Assert.DoesNotContain("-map 1:", args, StringComparison.Ordinal);
    }

    [Fact]
    public void GetMapArgs_InternalSrt_MapsFromPrimaryInput()
    {
        var sub = new MediaStream { Index = 2, Type = MediaStreamType.Subtitle, Codec = "srt" };
        var state = BuildState(sub, SubtitleDeliveryMethod.Embed);
        var args = CreateHelper().GetMapArgs(state);

        Assert.Contains("-map 0:2", args, StringComparison.Ordinal);
        Assert.DoesNotContain("-map 1:", args, StringComparison.Ordinal);
    }

    [Fact]
    public void GetMapArgs_InternalSubAtHigherIndex_MapsCorrectIndex()
    {
        var sub0 = new MediaStream { Index = 2, Type = MediaStreamType.Subtitle, Codec = "srt" };
        var sub1 = new MediaStream { Index = 3, Type = MediaStreamType.Subtitle, Codec = "ass" };
        var state = BuildState(sub1, SubtitleDeliveryMethod.Embed, additionalStreams: [sub0, sub1]);
        var args = CreateHelper().GetMapArgs(state);

        Assert.Contains("-map 0:3", args, StringComparison.Ordinal);
    }

    [Fact]
    public void GetMapArgs_ExternalSrt_MapsFirstStreamFromInput1()
    {
        var sub = new MediaStream
        {
            Index = 2,
            Type = MediaStreamType.Subtitle,
            Codec = "srt",
            IsExternal = true,
            SupportsExternalStream = true,
            Path = "/media/movie.en.srt"
        };
        var state = BuildState(sub, SubtitleDeliveryMethod.Embed);
        var args = CreateHelper().GetMapArgs(state);

        Assert.Contains("-map 1:0", args, StringComparison.Ordinal);
    }

    [Fact]
    public void GetMapArgs_SecondExternalSrt_StillMaps1Colon0()
    {
        // Two separate .srt files — selecting the second one still maps 1:0
        // because Jellyfin feeds only the selected file as ffmpeg input 1.
        var ext1 = new MediaStream
        {
            Index = 2,
            Type = MediaStreamType.Subtitle,
            Codec = "srt",
            IsExternal = true,
            SupportsExternalStream = true,
            Path = "/media/movie.en.srt"
        };
        var ext2 = new MediaStream
        {
            Index = 3,
            Type = MediaStreamType.Subtitle,
            Codec = "srt",
            IsExternal = true,
            SupportsExternalStream = true,
            Path = "/media/movie.fr.srt"
        };
        var state = BuildState(ext2, SubtitleDeliveryMethod.Embed, additionalStreams: [ext1, ext2]);
        var args = CreateHelper().GetMapArgs(state);

        Assert.Contains("-map 1:0", args, StringComparison.Ordinal);
    }

    [Fact]
    public void GetMapArgs_MksFirstTrack_MapsInFileIndex0()
    {
        var mks0 = new MediaStream
        {
            Index = 2,
            Type = MediaStreamType.Subtitle,
            Codec = "subrip",
            IsExternal = true,
            SupportsExternalStream = true,
            Path = "/media/movie.mks"
        };
        var mks1 = new MediaStream
        {
            Index = 3,
            Type = MediaStreamType.Subtitle,
            Codec = "ass",
            IsExternal = true,
            SupportsExternalStream = true,
            Path = "/media/movie.mks"
        };
        var state = BuildState(mks0, SubtitleDeliveryMethod.Embed, additionalStreams: [mks0, mks1]);
        var args = CreateHelper().GetMapArgs(state);

        Assert.Contains("-map 1:0", args, StringComparison.Ordinal);
    }

    [Fact]
    public void GetMapArgs_MksSecondTrack_MapsInFileIndex1()
    {
        var mks0 = new MediaStream
        {
            Index = 2,
            Type = MediaStreamType.Subtitle,
            Codec = "subrip",
            IsExternal = true,
            SupportsExternalStream = true,
            Path = "/media/movie.mks"
        };
        var mks1 = new MediaStream
        {
            Index = 3,
            Type = MediaStreamType.Subtitle,
            Codec = "ass",
            IsExternal = true,
            SupportsExternalStream = true,
            Path = "/media/movie.mks"
        };
        var mks2 = new MediaStream
        {
            Index = 4,
            Type = MediaStreamType.Subtitle,
            Codec = "subrip",
            IsExternal = true,
            SupportsExternalStream = true,
            Path = "/media/movie.mks"
        };
        var state = BuildState(mks1, SubtitleDeliveryMethod.Embed, additionalStreams: [mks0, mks1, mks2]);
        var args = CreateHelper().GetMapArgs(state);

        Assert.Contains("-map 1:1", args, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SubtitleDeliveryMethod.Embed, true, "movie.idx")]
    [InlineData(SubtitleDeliveryMethod.Encode, true, "movie.idx")]
    [InlineData(SubtitleDeliveryMethod.Embed, false, "movie.sub")]
    [InlineData(SubtitleDeliveryMethod.Encode, false, "movie.sub")]
    public void GetInputArgument_VobSub_UsesCorrectPath(
        SubtitleDeliveryMethod deliveryMethod,
        bool createIdxFile,
        string expectedFilename)
    {
        var tempDir = Directory.CreateTempSubdirectory("jellyfin-test-");
        try
        {
            var subFile = Path.Combine(tempDir.FullName, "movie.sub");
            File.WriteAllText(subFile, "dummy");

            if (createIdxFile)
            {
                File.WriteAllText(Path.Combine(tempDir.FullName, "movie.idx"), "dummy");
            }

            var sub = new MediaStream
            {
                Index = 2,
                Type = MediaStreamType.Subtitle,
                Codec = "dvdsub",
                IsExternal = true,
                SupportsExternalStream = true,
                Path = subFile
            };
            var state = BuildState(sub, deliveryMethod);
            var inputArgs = CreateHelper().GetInputArgument(state, new EncodingOptions(), null);

            Assert.Contains(expectedFilename, inputArgs, StringComparison.Ordinal);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Theory]
    [InlineData("aac", 44100, 44100)] // non-opus: requested rate must be preserved (issue #17026)
    [InlineData("aac", 48000, 48000)]
    [InlineData("mp3", 22050, 22050)]
    [InlineData("flac", 96000, 96000)]
    [InlineData("opus", 44100, 48000)] // opus: must snap to a libopus-supported rate
    [InlineData("opus", 22050, 24000)]
    [InlineData("opus", 8000, 8000)]
    public void GetProgressiveAudioFullCommandLine_SampleRate_OnlyClampedForOpus(
        string audioCodec,
        int requestedSampleRate,
        int expectedSampleRate)
    {
        var state = BuildAudioState(audioCodec, requestedSampleRate);
        var args = CreateHelper().GetProgressiveAudioFullCommandLine(state, new EncodingOptions(), "/tmp/out");

        Assert.Contains("-ar " + expectedSampleRate, args, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("wav")]
    [InlineData("flac")]
    [InlineData("mp3")]
    public void GetProgressiveAudioFullCommandLine_PcmInRealContainer_KeepsContainerMuxer(string outputContainer)
    {
        // A pcm_* encoder must not drag the raw muxer into a container that writes its own header,
        // or the client gets headerless PCM behind the container's content type.
        var state = BuildAudioState("pcm_s16le", 48000, outputContainer);
        var args = CreateHelper().GetProgressiveAudioFullCommandLine(state, new EncodingOptions(), "/tmp/out");

        Assert.DoesNotContain("-f s16le", args, StringComparison.Ordinal);
    }

    [Fact]
    public void GetProgressiveAudioFullCommandLine_PcmInPcmContainer_ForcesRawMuxer()
    {
        // The raw-PCM route added in #10321 for I2S/MCU clients must keep working.
        var state = BuildAudioState("pcm_s16le", 48000, "pcm");
        var args = CreateHelper().GetProgressiveAudioFullCommandLine(state, new EncodingOptions(), "/tmp/out");

        Assert.Contains("-f s16le", args, StringComparison.Ordinal);
    }

    [Fact]
    public void GetProgressiveAudioFullCommandLine_PcmWithoutBitrate_EmitsNoEmptySampleRate()
    {
        // AudioBitRate is optional; it used to be emitted as `-ar <null>`, producing a bare `-ar`
        // that made ffmpeg abort with "Expected number for ar" and the request fail with HTTP 500.
        var state = BuildAudioState("pcm_s16le", 48000, "wav");
        state.BaseRequest.AudioBitRate = null;
        var args = CreateHelper().GetProgressiveAudioFullCommandLine(state, new EncodingOptions(), "/tmp/out");

        Assert.DoesNotContain("-ar -", args, StringComparison.Ordinal);
        Assert.DoesNotContain("-ar  ", args, StringComparison.Ordinal);
        Assert.Contains("-ar 48000", args, StringComparison.Ordinal);
    }

    [Fact]
    public void EnforceResolutionLimit_WithoutUpscaling_ConvertsDimensionsToCeilings()
    {
        var state = BuildState(subtitle: null, deliveryMethod: null);
        state.BaseRequest.Width = 1920;
        state.BaseRequest.Height = 1080;

        CreateHelper().EnforceResolutionLimit(state);

        Assert.Null(state.BaseRequest.Width);
        Assert.Null(state.BaseRequest.Height);
        Assert.Equal(1920, state.BaseRequest.MaxWidth);
        Assert.Equal(1080, state.BaseRequest.MaxHeight);
    }

    [Fact]
    public void EnforceResolutionLimit_VideoUpscaling_PreservesExactDimensions()
    {
        var state = BuildState(subtitle: null, deliveryMethod: null);
        state.VideoStream!.Width = 1920;
        state.VideoStream.Height = 1080;
        state.VideoStream.BitRate = 4_000_000;
        state.BaseRequest.Width = 3840;
        state.BaseRequest.Height = 2160;
        state.BaseRequest.VideoUpscalingMode = VideoUpscalingMode.Fsr;
        state.BaseRequest.VideoBitRate = 20_000_000;

        CreateHelper().EnforceResolutionLimit(state);

        Assert.Equal(3840, state.BaseRequest.Width);
        Assert.Equal(2160, state.BaseRequest.Height);
        Assert.Null(state.BaseRequest.MaxWidth);
        Assert.Null(state.BaseRequest.MaxHeight);
        Assert.Equal(3840, state.OutputWidth);
        Assert.Equal(2160, state.OutputHeight);
        Assert.Equal(20_000_000, CreateHelper().GetVideoBitrateParamValue(state.BaseRequest, state.VideoStream, "h264"));
    }

    [Theory]
    [InlineData(VideoUpscalingMode.Fsr, "FSR.glsl")]
    [InlineData(VideoUpscalingMode.Anime4KModeA, "Anime4K_Mode_A.glsl")]
    [InlineData(VideoUpscalingMode.Anime4KModeB, "Anime4K_Mode_B.glsl")]
    [InlineData(VideoUpscalingMode.Anime4KModeC, "Anime4K_Mode_C.glsl")]
    [InlineData(VideoUpscalingMode.Anime4KModeAPlusA, "Anime4K_Mode_A+A.glsl")]
    [InlineData(VideoUpscalingMode.Anime4KModeBPlusB, "Anime4K_Mode_B+B.glsl")]
    [InlineData(VideoUpscalingMode.Anime4KModeCPlusA, "Anime4K_Mode_C+A.glsl")]
    public void GetNvidiaVidFiltersPrefered_VideoUpscaling_UsesSelectedShaderAndMpvDefaults(
        VideoUpscalingMode mode,
        string shaderFileName)
    {
        var shaderDirectory = Directory.CreateTempSubdirectory("jellyfin-upscale-test-");
        try
        {
            var shaderPath = Path.Combine(shaderDirectory.FullName, shaderFileName);
            File.WriteAllText(shaderPath, "//!HOOK MAIN");

            var mediaEncoder = new Mock<IMediaEncoder>();
            mediaEncoder.Setup(i => i.SupportsHwaccel("vulkan")).Returns(true);
            mediaEncoder.Setup(i => i.SupportsFilter("libplacebo")).Returns(true);
            mediaEncoder.Setup(i => i.SupportsFilter("hwupload_cuda")).Returns(true);
            mediaEncoder.Setup(i => i.EscapeSubtitleFilterPath(shaderPath)).Returns(shaderPath);

            var state = BuildState(subtitle: null, deliveryMethod: null);
            state.VideoStream!.Width = 1920;
            state.VideoStream.Height = 1080;
            state.BaseRequest.Width = 3840;
            state.BaseRequest.Height = 2160;
            state.BaseRequest.VideoUpscalingMode = mode;

            var options = new EncodingOptions
            {
                HardwareAccelerationType = HardwareAccelerationType.nvenc,
                VideoUpscalingShaderDirectory = shaderDirectory.FullName
            };

            var filters = CreateHelper(mediaEncoder.Object).GetNvidiaVidFiltersPrefered(state, options, "h264_cuda", "h264_nvenc");
            var filter = string.Join(',', filters.MainFilters);

            Assert.Contains("libplacebo=w=3840:h=2160", filter, StringComparison.Ordinal);
            Assert.Contains("upscaler=ewa_lanczos4sharpest", filter, StringComparison.Ordinal);
            Assert.Contains("downscaler=catmull_rom", filter, StringComparison.Ordinal);
            Assert.Contains("antiringing=1", filter, StringComparison.Ordinal);
            Assert.Contains("deband=1:deband_iterations=4:deband_threshold=48:deband_radius=24:deband_grain=16", filter, StringComparison.Ordinal);
            Assert.Contains("plane_upscaler=ewa_lanczos4sharpest", filter, StringComparison.Ordinal);
            Assert.Contains(shaderPath, filter, StringComparison.Ordinal);
            Assert.Contains("hwupload_cuda", filters.MainFilters);
            Assert.DoesNotContain(filters.MainFilters, item => item.StartsWith("scale_cuda", StringComparison.Ordinal));
        }
        finally
        {
            shaderDirectory.Delete(true);
        }
    }

    [Fact]
    public void GetNvidiaVidFiltersPrefered_VideoUpscaling_AppliesCustomRenderingOptions()
    {
        var shaderDirectory = Directory.CreateTempSubdirectory("jellyfin-upscale-test-");
        try
        {
            var shaderPath = Path.Combine(shaderDirectory.FullName, "FSR.glsl");
            File.WriteAllText(shaderPath, "//!HOOK MAIN");

            var mediaEncoder = new Mock<IMediaEncoder>();
            mediaEncoder.Setup(i => i.SupportsHwaccel("vulkan")).Returns(true);
            mediaEncoder.Setup(i => i.SupportsFilter("libplacebo")).Returns(true);
            mediaEncoder.Setup(i => i.SupportsFilter("hwupload_cuda")).Returns(true);
            mediaEncoder.Setup(i => i.EscapeSubtitleFilterPath(shaderPath)).Returns(shaderPath);

            var state = BuildState(subtitle: null, deliveryMethod: null);
            state.VideoStream!.Width = 1280;
            state.VideoStream.Height = 720;
            state.BaseRequest.Width = 1920;
            state.BaseRequest.Height = 1080;
            state.BaseRequest.VideoUpscalingMode = VideoUpscalingMode.Fsr;
            state.BaseRequest.StreamOptions["videoUpscalingScaler"] = "bilinear";
            state.BaseRequest.StreamOptions["videoUpscalingDownscaler"] = "mitchell";
            state.BaseRequest.StreamOptions["videoUpscalingChromaScaler"] = "catmull_rom";
            state.BaseRequest.StreamOptions["videoUpscalingAntiringing"] = "0.5";
            state.BaseRequest.StreamOptions["videoUpscalingDeband"] = "false";
            state.BaseRequest.StreamOptions["videoUpscalingDebandIterations"] = "2";
            state.BaseRequest.StreamOptions["videoUpscalingDebandThreshold"] = "16";
            state.BaseRequest.StreamOptions["videoUpscalingDebandRadius"] = "8";
            state.BaseRequest.StreamOptions["videoUpscalingDebandGrain"] = "4";

            var options = new EncodingOptions
            {
                HardwareAccelerationType = HardwareAccelerationType.nvenc,
                VideoUpscalingShaderDirectory = shaderDirectory.FullName
            };

            var filters = CreateHelper(mediaEncoder.Object).GetNvidiaVidFiltersPrefered(state, options, "h264_cuda", "h264_nvenc");
            var filter = string.Join(',', filters.MainFilters);

            Assert.Contains("upscaler=bilinear:downscaler=mitchell:antiringing=0.5", filter, StringComparison.Ordinal);
            Assert.Contains("deband=0:deband_iterations=2:deband_threshold=16:deband_radius=8:deband_grain=4", filter, StringComparison.Ordinal);
            Assert.Contains("plane_upscaler=catmull_rom", filter, StringComparison.Ordinal);
        }
        finally
        {
            shaderDirectory.Delete(true);
        }
    }

    private static EncodingJobInfo BuildAudioState(string audioCodec, int requestedSampleRate, string? outputContainer = null)
    {
        var audio = new MediaStream { Index = 0, Type = MediaStreamType.Audio, Codec = "flac", SampleRate = 96000 };

        return new EncodingJobInfo(TranscodingJobType.Progressive)
        {
            OutputContainer = outputContainer,
            MediaSource = new MediaSourceInfo
            {
                Container = "flac",
                MediaStreams = new List<MediaStream> { audio },
                Path = "/media/track.flac",
                Protocol = MediaProtocol.File,
            },
            AudioStream = audio,
            OutputAudioCodec = audioCodec,
            BaseRequest = new VideoRequestDto
            {
                AudioCodec = audioCodec,
                AudioSampleRate = requestedSampleRate,
            },
            IsVideoRequest = false,
            IsInputVideo = false,
        };
    }

    private static EncodingJobInfo BuildState(
        MediaStream? subtitle,
        SubtitleDeliveryMethod? deliveryMethod,
        MediaStream[]? additionalStreams = null)
    {
        var video = new MediaStream { Index = 0, Type = MediaStreamType.Video, Codec = "h264" };
        var audio = new MediaStream { Index = 1, Type = MediaStreamType.Audio, Codec = "aac" };
        var streams = new List<MediaStream> { video, audio };

        if (additionalStreams is not null)
        {
            streams.AddRange(additionalStreams);
        }
        else if (subtitle is not null)
        {
            streams.Add(subtitle);
        }

        return new EncodingJobInfo(TranscodingJobType.Progressive)
        {
            MediaSource = new MediaSourceInfo
            {
                Container = "mkv",
                MediaStreams = streams,
            },
            VideoStream = video,
            AudioStream = audio,
            SubtitleStream = subtitle,
            SubtitleDeliveryMethod = deliveryMethod ?? SubtitleDeliveryMethod.Drop,
            BaseRequest = new VideoRequestDto(),
            IsVideoRequest = true,
            IsInputVideo = true,
        };
    }

    private static EncodingHelper CreateHelper(IMediaEncoder? mediaEncoder = null)
    {
        var appPaths = Mock.Of<IApplicationPaths>();
        var subtitleEncoder = new Mock<ISubtitleEncoder>();
        var config = new Mock<IConfiguration>();
        var configurationManager = new Mock<IConfigurationManager>();
        var pathManager = new Mock<IPathManager>();

        return new EncodingHelper(
            appPaths,
            mediaEncoder ?? Mock.Of<IMediaEncoder>(),
            subtitleEncoder.Object,
            config.Object,
            configurationManager.Object,
            pathManager.Object);
    }
}
