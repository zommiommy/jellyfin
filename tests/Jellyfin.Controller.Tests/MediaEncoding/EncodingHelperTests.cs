using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    [Theory]
    [InlineData(1920, 1080, 3840, 2160, VideoUpscalingMode.NvidiaVsrQUltra, "qultra", 2, false, false)]
    [InlineData(1280, 720, 3840, 2160, VideoUpscalingMode.NvidiaVsrLow, "low", 2, false, false)]
    [InlineData(720, 480, 2880, 2160, VideoUpscalingMode.NvidiaVsrUltra, "ultra", 4, true, false)]
    [InlineData(1920, 1080, 1920, 1080, VideoUpscalingMode.None, "none", 1, false, true)]
    [InlineData(1280, 720, 3840, 2160, VideoUpscalingMode.NvidiaVsrQHigh, "qhigh", 3, false, true)]
    [InlineData(640, 480, 2560, 1920, VideoUpscalingMode.NvidiaVsrHigh, "high", 4, false, false)]
    [InlineData(1280, 720, 1920, 1080, VideoUpscalingMode.NvidiaVsrMedium, "medium", 2, true, false)]
    [InlineData(1280, 720, 1920, 1080, VideoUpscalingMode.NvidiaVsrQLow, "qlow", 2, false, false)]
    [InlineData(1280, 720, 1920, 1080, VideoUpscalingMode.NvidiaVsrQMedium, "qmedium", 2, false, false)]
    [InlineData(1280, 720, 1920, 1080, VideoUpscalingMode.NvidiaVsrHigh, "high", 2, false, true)]
    public void GetNvidiaVidFiltersPrefered_RtxVideo_SelectsNativeHeadAndExactOutput(
        int width,
        int height,
        int outputWidth,
        int outputHeight,
        VideoUpscalingMode mode,
        string model,
        int scale,
        bool softwareDecoder,
        bool trueHdr)
    {
        var directory = CreateRtxModels();
        try
        {
            var encoder = CreateRtxEncoder();
            var helper = CreateHelper(encoder.Object);
            var state = BuildRtxState(width, height, outputWidth, outputHeight, mode, trueHdr);
            if (width == 720 && height == 480)
            {
                state.VideoStream!.AspectRatio = "4:3";
                state.VideoStream.IsAnamorphic = true;
            }

            var options = new EncodingOptions { HardwareAccelerationType = HardwareAccelerationType.nvenc, NvidiaRtxVideoModelDirectory = directory.FullName };
            var codec = trueHdr ? "hevc_nvenc" : "h264_nvenc";
            var filters = helper.GetNvidiaVidFiltersPrefered(state, options, softwareDecoder ? string.Empty : "h264_cuda", codec);
            var actual = string.Join(',', filters.MainFilters.Where(filter => !string.IsNullOrEmpty(filter)));
            var expected = softwareDecoder ? "format=nv12,hwupload_cuda," : string.Empty;
            expected += "rtxvideo_cuda=";
            if (mode != VideoUpscalingMode.None)
            {
                expected += $"vsr_models='{directory.FullName}/vsr.zip':";
            }

            expected += $"vsr={model}:scale={scale}";
            if (trueHdr)
            {
                expected += $":truehdr_models='{directory.FullName}/truehdr.zip':truehdr=1:truehdr_peak=1000:truehdr_contrast=100:truehdr_saturation=100:truehdr_middle_gray=50";
            }

            expected += $",scale_cuda=w={outputWidth}:h={outputHeight}:interp_algo=lanczos:format={(trueHdr ? "p010" : "yuv420p")}";
            if (mode != VideoUpscalingMode.None)
            {
                expected += ",setsar=1";
            }

            Assert.EndsWith(expected, actual, StringComparison.Ordinal);
            Assert.DoesNotContain("hwdownload", actual, StringComparison.Ordinal);
            Assert.DoesNotContain("libplacebo", actual, StringComparison.Ordinal);
            Assert.Empty(filters.OverlayFilters);
            var quality = helper.GetVideoQualityParam(state, codec, options, EncoderPreset.veryfast);
            if (trueHdr)
            {
                Assert.Contains("-profile:v:0 main10", quality, StringComparison.Ordinal);
            }

            TestContext.Current.TestOutputHelper!.WriteLine($"{width}x{height} {model} software={softwareDecoder} truehdr={trueHdr}: {actual.Replace(directory.FullName, "/models", StringComparison.Ordinal)}; -c:v {codec}{quality}");
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void GetNvidiaVidFiltersPrefered_RtxVideo_KeepsSourceColorTagsForTheInputMatrix(bool softwareDecoder, bool trueHdr)
    {
        var directory = CreateRtxModels();
        try
        {
            var state = BuildRtxState(1280, 720, 3840, 2160, VideoUpscalingMode.NvidiaVsrQHigh, trueHdr);
            state.VideoStream!.ColorSpace = "smpte170m";
            state.VideoStream.ColorPrimaries = "smpte170m";
            state.VideoStream.ColorTransfer = "smpte170m";
            var filters = CreateHelper(CreateRtxEncoder().Object).GetNvidiaVidFiltersPrefered(
                state,
                new EncodingOptions { HardwareAccelerationType = HardwareAccelerationType.nvenc, NvidiaRtxVideoModelDirectory = directory.FullName },
                softwareDecoder ? string.Empty : "h264_cuda",
                trueHdr ? "hevc_nvenc" : "h264_nvenc");

            // rtxvideo_cuda picks its YUV to RGB matrix from the frame tags; a retag would change the decoded colors.
            Assert.DoesNotContain(filters.MainFilters, filter => filter.StartsWith("setparams", StringComparison.Ordinal));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void GetNvidiaVidFiltersPrefered_RtxVideo_CompositesSubtitlesInTheCorrectColorSpace(bool softwareDecoder, bool trueHdr)
    {
        var directory = CreateRtxModels();
        try
        {
            var state = BuildRtxState(1280, 720, 3840, 2160, VideoUpscalingMode.NvidiaVsrQHigh, trueHdr);
            state.SubtitleStream = new MediaStream { Type = MediaStreamType.Subtitle, Codec = "hdmv_pgs_subtitle", Width = 1920, Height = 1080 };
            state.SubtitleDeliveryMethod = SubtitleDeliveryMethod.Encode;
            var options = new EncodingOptions { HardwareAccelerationType = HardwareAccelerationType.nvenc, NvidiaRtxVideoModelDirectory = directory.FullName };
            var filters = CreateHelper(CreateRtxEncoder().Object).GetNvidiaVidFiltersPrefered(state, options, softwareDecoder ? string.Empty : "h264_cuda", trueHdr ? "hevc_nvenc" : "h264_nvenc");
            if (trueHdr)
            {
                Assert.Equal(!softwareDecoder, filters.MainFilters.Contains("hwdownload"));
                Assert.Contains(filters.SubFilters, filter => filter.Contains("1280", StringComparison.Ordinal) && filter.Contains("720", StringComparison.Ordinal));
                Assert.Equal("overlay=eof_action=pass:repeatlast=0", filters.OverlayFilters[0]);
                Assert.Equal("format=nv12", filters.OverlayFilters[1]);
                Assert.Equal("hwupload_cuda", filters.OverlayFilters[2]);
                Assert.StartsWith("rtxvideo_cuda=", filters.OverlayFilters[3], StringComparison.Ordinal);
                Assert.Contains("format=p010", filters.OverlayFilters[4], StringComparison.Ordinal);
            }
            else
            {
                Assert.DoesNotContain("hwdownload", filters.MainFilters);
                Assert.Contains(filters.MainFilters, filter => filter.StartsWith("rtxvideo_cuda=", StringComparison.Ordinal));
                Assert.Contains(filters.MainFilters, filter => filter.Contains("scale_cuda=w=3840:h=2160", StringComparison.Ordinal));
                Assert.Contains(filters.SubFilters, filter => filter.Contains("3840", StringComparison.Ordinal) && filter.Contains("2160", StringComparison.Ordinal));
                Assert.Equal("overlay_cuda=eof_action=pass:repeatlast=0", Assert.Single(filters.OverlayFilters));
            }

            TestContext.Current.TestOutputHelper!.WriteLine($"software={softwareDecoder} truehdr={trueHdr}; main={string.Join(',', filters.MainFilters)}; sub={string.Join(',', filters.SubFilters)}; overlay={string.Join(',', filters.OverlayFilters)}");
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void GetNvidiaVidFiltersPrefered_RtxVideo_BurnsTextBeforeTrueHdrButAfterSdrUpscaling(bool trueHdr, bool softwareDecoder)
    {
        var directory = CreateRtxModels();
        try
        {
            var state = BuildRtxState(1920, 1080, 3840, 2160, VideoUpscalingMode.NvidiaVsrQUltra, trueHdr);
            state.SubtitleStream = new MediaStream { Type = MediaStreamType.Subtitle, Codec = "srt", IsExternal = true, Path = "/media/test.srt" };
            state.SubtitleDeliveryMethod = SubtitleDeliveryMethod.Encode;
            var filters = CreateHelper(CreateRtxEncoder().Object).GetNvidiaVidFiltersPrefered(
                state,
                new EncodingOptions { HardwareAccelerationType = HardwareAccelerationType.nvenc, NvidiaRtxVideoModelDirectory = directory.FullName },
                softwareDecoder ? string.Empty : "h264_cuda",
                trueHdr ? "hevc_nvenc" : "h264_nvenc");
            var actual = string.Join(',', filters.MainFilters.Where(filter => !string.IsNullOrEmpty(filter)));
            if (trueHdr)
            {
                Assert.Contains("subtitles=f='/media/test.srt',setpts=PTS -0/TB,format=nv12,hwupload_cuda,rtxvideo_cuda=", actual, StringComparison.Ordinal);
                Assert.Contains("format=p010", actual, StringComparison.Ordinal);
            }
            else
            {
                Assert.DoesNotContain("subtitles=", actual, StringComparison.Ordinal);
                Assert.DoesNotContain("hwdownload", actual, StringComparison.Ordinal);
                Assert.Contains("scale_cuda=w=3840:h=2160:interp_algo=lanczos:format=yuv420p", actual, StringComparison.Ordinal);
                Assert.Contains(filters.SubFilters, filter => filter.StartsWith("alphasrc=", StringComparison.Ordinal) && filter.Contains("3840x2160", StringComparison.Ordinal));
                Assert.Contains(filters.SubFilters, filter => filter.Contains("subtitles=f='/media/test.srt':alpha=1:sub2video=1", StringComparison.Ordinal));
                Assert.Equal("overlay_cuda=eof_action=pass:repeatlast=0", Assert.Single(filters.OverlayFilters));
            }

            TestContext.Current.TestOutputHelper!.WriteLine($"software={softwareDecoder} truehdr={trueHdr}; main={actual}; sub={string.Join(',', filters.SubFilters)}; overlay={string.Join(',', filters.OverlayFilters)}");
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void GetNvidiaVidFiltersPrefered_RtxVideo_AppliesControlsAndIgnoresLibplaceboOptions()
    {
        var directory = CreateRtxModels();
        try
        {
            var state = BuildRtxState(1280, 720, 3840, 2160, VideoUpscalingMode.NvidiaVsrQHigh, true);
            state.BaseRequest.StreamOptions["videoTrueHdrPeakBrightness"] = "1600";
            state.BaseRequest.StreamOptions["videoTrueHdrContrast"] = "0";
            state.BaseRequest.StreamOptions["videoTrueHdrSaturation"] = "200";
            state.BaseRequest.StreamOptions["videoTrueHdrMiddleGray"] = "10";
            state.BaseRequest.StreamOptions["videoUpscalingScaler"] = "not-a-scaler";
            var filters = CreateHelper(CreateRtxEncoder().Object).GetNvidiaVidFiltersPrefered(
                state,
                new EncodingOptions { HardwareAccelerationType = HardwareAccelerationType.nvenc, NvidiaRtxVideoModelDirectory = directory.FullName },
                "h264_cuda",
                "hevc_nvenc");
            Assert.Contains(filters.MainFilters, filter => filter.Contains(":truehdr_peak=1600:truehdr_contrast=0:truehdr_saturation=200:truehdr_middle_gray=10", StringComparison.Ordinal));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void GetNvidiaVidFiltersPrefered_RtxVideo_ReportsMissingPack()
    {
        var directory = Directory.CreateTempSubdirectory("jellyfin-missing-rtx-");
        try
        {
            var state = BuildRtxState(1920, 1080, 3840, 2160, VideoUpscalingMode.NvidiaVsrUltra, false);
            var exception = Assert.Throws<FileNotFoundException>(() => CreateHelper(CreateRtxEncoder().Object).GetNvidiaVidFiltersPrefered(
                state,
                new EncodingOptions { HardwareAccelerationType = HardwareAccelerationType.nvenc, NvidiaRtxVideoModelDirectory = directory.FullName },
                "h264_cuda",
                "h264_nvenc"));
            Assert.Equal(Path.Combine(directory.FullName, "vsr.zip"), exception.FileName);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Theory]
    [InlineData("h264_nvenc", false)]
    [InlineData("libx264", true)]
    public void GetNvidiaVidFilterChain_RtxVideo_RejectsMissingCapabilityInsteadOfSkippingEnhancement(string codec, bool supportsFilter)
    {
        var encoder = CreateRtxEncoder();
        encoder.Setup(value => value.SupportsFilter("rtxvideo_cuda")).Returns(supportsFilter);
        var state = BuildRtxState(1920, 1080, 3840, 2160, VideoUpscalingMode.NvidiaVsrQUltra, false);
        Assert.Throws<NotSupportedException>(() => CreateHelper(encoder.Object).GetNvidiaVidFilterChain(state, new EncodingOptions { HardwareAccelerationType = HardwareAccelerationType.nvenc }, codec));
    }

    [Fact]
    public void GetInputVideoHwaccelArgs_RtxVideo_InitializesOnlyCuda()
    {
        var encoder = CreateRtxEncoder();
        encoder.Setup(value => value.SupportsEncoder("h264_nvenc")).Returns(true);
        encoder.Setup(value => value.SupportsHwaccel("cuda")).Returns(true);
        encoder.Setup(value => value.SupportsFilter("yadif_cuda")).Returns(true);
        encoder.Setup(value => value.SupportsFilter("overlay_cuda")).Returns(true);
        encoder.Setup(value => value.SupportsFilterWithOption(FilterOptionType.ScaleCudaFormat)).Returns(true);
        encoder.Setup(value => value.SupportsFilterWithOption(FilterOptionType.TonemapCudaName)).Returns(true);
        var state = BuildRtxState(1920, 1080, 3840, 2160, VideoUpscalingMode.NvidiaVsrQUltra, false);
        state.VideoType = VideoType.VideoFile;
        var args = CreateHelper(encoder.Object).GetInputVideoHwaccelArgs(state, new EncodingOptions { HardwareAccelerationType = HardwareAccelerationType.nvenc });
        Assert.Contains("-init_hw_device cuda=", args, StringComparison.Ordinal);
        Assert.Contains("-filter_hw_device", args, StringComparison.Ordinal);
        Assert.DoesNotContain("vulkan", args, StringComparison.Ordinal);
    }

    [Fact]
    public void GetNvidiaVidFiltersPrefered_RtxVideo_RotatesBeforeSelectingAndRunningTheNativeHead()
    {
        var directory = CreateRtxModels();
        try
        {
            var encoder = CreateRtxEncoder();
            encoder.Setup(value => value.SupportsFilter("transpose_cuda")).Returns(true);
            var state = BuildRtxState(1080, 1920, 3840, 2160, VideoUpscalingMode.NvidiaVsrQUltra, false);
            state.VideoStream!.Rotation = 90;
            var filters = CreateHelper(encoder.Object).GetNvidiaVidFiltersPrefered(
                state,
                new EncodingOptions { HardwareAccelerationType = HardwareAccelerationType.nvenc, NvidiaRtxVideoModelDirectory = directory.FullName },
                "h264_cuda",
                "h264_nvenc");
            var filter = string.Join(',', filters.MainFilters.Where(value => !string.IsNullOrEmpty(value)));
            Assert.Contains("transpose_cuda=dir=cclock,rtxvideo_cuda=", filter, StringComparison.Ordinal);
            Assert.Contains(":vsr=qultra:scale=2,scale_cuda=w=3840:h=2160", filter, StringComparison.Ordinal);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void CanStreamCopyVideo_TrueHdr_DisallowsCopyEvenForCompatibleHevc()
    {
        var state = BuildRtxState(1920, 1080, 1920, 1080, VideoUpscalingMode.None, true);
        state.VideoStream!.Codec = "hevc";
        state.BaseRequest.AllowVideoStreamCopy = true;
        Assert.False(CreateHelper().CanStreamCopyVideo(state, state.VideoStream));
    }

    private static DirectoryInfo CreateRtxModels()
    {
        var directory = Directory.CreateTempSubdirectory("jellyfin-rtx-");
        File.WriteAllBytes(Path.Combine(directory.FullName, "vsr.zip"), []);
        File.WriteAllBytes(Path.Combine(directory.FullName, "truehdr.zip"), []);
        return directory;
    }

    private static Mock<IMediaEncoder> CreateRtxEncoder()
    {
        var encoder = new Mock<IMediaEncoder>();
        encoder.Setup(i => i.SupportsFilter("rtxvideo_cuda")).Returns(true);
        encoder.Setup(i => i.SupportsFilter("hwupload_cuda")).Returns(true);
        encoder.Setup(i => i.EscapeSubtitleFilterPath(It.IsAny<string>())).Returns((string path) => path);
        return encoder;
    }

    private static EncodingJobInfo BuildRtxState(int width, int height, int outputWidth, int outputHeight, VideoUpscalingMode mode, bool trueHdr)
    {
        var state = BuildState(null, null);
        state.VideoStream!.Width = width;
        state.VideoStream.Height = height;
        state.VideoStream.RealFrameRate = 24;
        state.VideoStream.BitRate = 4_000_000;
        state.BaseRequest.Width = outputWidth;
        state.BaseRequest.Height = outputHeight;
        state.BaseRequest.VideoUpscalingMode = mode;
        state.BaseRequest.VideoBitRate = 20_000_000;
        state.BaseRequest.StreamOptions["videoTrueHdr"] = trueHdr.ToString();
        state.OutputVideoCodec = trueHdr ? "hevc" : "h264";
        state.OutputVideoBitrate = 20_000_000;
        return state;
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
