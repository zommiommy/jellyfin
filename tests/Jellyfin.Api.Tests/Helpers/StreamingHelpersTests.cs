using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Helpers;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.Helpers;

public class StreamingHelpersTests
{
    private readonly Guid _itemId = Guid.NewGuid();
    private readonly Mock<IMediaSourceManager> _mediaSourceManager = new();
    private readonly Mock<ILibraryManager> _libraryManager = new();
    private readonly Mock<IServerConfigurationManager> _configurationManager = new();
    private readonly IMediaEncoder _mediaEncoder;
    private readonly EncodingHelper _encodingHelper;

    public StreamingHelpersTests()
    {
        var mediaSource = new MediaSourceInfo
        {
            Id = "video-source",
            Path = "/media/video.mkv",
            Protocol = MediaProtocol.File,
            Container = "mkv",
            MediaStreams =
            [
                new MediaStream
                {
                    Type = MediaStreamType.Video,
                    Index = 0,
                    Codec = "h264",
                    Width = 1920,
                    Height = 1080,
                    BitDepth = 8,
                    ColorTransfer = "bt709",
                    ColorPrimaries = "bt709",
                    ColorSpace = "bt709",
                    RealFrameRate = 24
                },
                new MediaStream
                {
                    Type = MediaStreamType.Audio,
                    Index = 1,
                    Codec = "aac",
                    Channels = 2,
                    SampleRate = 48000,
                    BitRate = 192000
                }
            ]
        };
        _libraryManager.Setup(x => x.GetItemById<BaseItem>(_itemId)).Returns(new Movie { Id = _itemId });
        _mediaSourceManager
            .Setup(x => x.GetPlaybackMediaSources(It.IsAny<BaseItem>(), It.IsAny<User>(), false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { mediaSource });
        _configurationManager.Setup(x => x.GetConfiguration("encoding"))
            .Returns(new EncodingOptions { TranscodingTempPath = Path.GetTempPath() });
        _configurationManager.SetupGet(x => x.CommonApplicationPaths).Returns(Mock.Of<IApplicationPaths>());

        var mediaEncoder = new Mock<IMediaEncoder>();
        mediaEncoder.Setup(x => x.CanEncodeToAudioCodec(It.IsAny<string>())).Returns(true);
        _mediaEncoder = mediaEncoder.Object;
        _encodingHelper = new EncodingHelper(
            Mock.Of<IApplicationPaths>(),
            _mediaEncoder,
            Mock.Of<ISubtitleEncoder>(),
            new ConfigurationBuilder().Build(),
            _configurationManager.Object,
            Mock.Of<IPathManager>());
    }

    [Theory]
    [InlineData(VideoUpscalingMode.NvidiaVsrLow, 400, 0, 0, 10)]
    [InlineData(VideoUpscalingMode.NvidiaVsrQUltra, 2000, 200, 200, 100)]
    public async Task GetStreamingState_TrueHdrUrl_RoundTripsTuningAndUpscalingMode(VideoUpscalingMode mode, int peakBrightness, int contrast, int saturation, int middleGray)
    {
        var streamInfo = CreateStreamInfo();
        streamInfo.VideoUpscalingMode = mode;
        streamInfo.VideoTrueHdr = true;
        streamInfo.VideoTrueHdrPeakBrightness = peakBrightness;
        streamInfo.VideoTrueHdrContrast = contrast;
        streamInfo.VideoTrueHdrSaturation = saturation;
        streamInfo.VideoTrueHdrMiddleGray = middleGray;
        var url = streamInfo.ToUrl("https://media.example.com", null, null);

        using var state = await GetStreamingState(new Uri(url).Query);

        Assert.Equal(mode, state.Request.VideoUpscalingMode);
        Assert.True(state.Request.VideoTrueHdr);
        Assert.Equal(peakBrightness, state.Request.VideoTrueHdrPeakBrightness);
        Assert.Equal(contrast, state.Request.VideoTrueHdrContrast);
        Assert.Equal(saturation, state.Request.VideoTrueHdrSaturation);
        Assert.Equal(middleGray, state.Request.VideoTrueHdrMiddleGray);
    }

    [Fact]
    public async Task GetStreamingState_UrlWithoutTrueHdr_UsesDocumentedDefaults()
    {
        var url = CreateStreamInfo().ToUrl("https://media.example.com", null, null);

        using var state = await GetStreamingState(new Uri(url).Query);

        Assert.False(state.Request.VideoTrueHdr);
        Assert.Equal(1000, state.Request.VideoTrueHdrPeakBrightness);
        Assert.Equal(100, state.Request.VideoTrueHdrContrast);
        Assert.Equal(100, state.Request.VideoTrueHdrSaturation);
        Assert.Equal(50, state.Request.VideoTrueHdrMiddleGray);
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("TrUe", true)]
    public async Task GetStreamingState_TrueHdrBoolean_ParsesExplicitValues(string value, bool expected)
    {
        using var state = await GetStreamingState("?videoTrueHdr=" + value);

        Assert.Equal(expected, state.Request.VideoTrueHdr);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("on")]
    public async Task GetStreamingState_InvalidTrueHdrBoolean_Throws(string value)
    {
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            using var state = await GetStreamingState("?videoTrueHdr=" + value);
            _ = state.Request.VideoTrueHdr;
        });
    }

    [Theory]
    [InlineData("videoTrueHdrPeakBrightness", "399")]
    [InlineData("videoTrueHdrPeakBrightness", "2001")]
    [InlineData("videoTrueHdrContrast", "-1")]
    [InlineData("videoTrueHdrContrast", "201")]
    [InlineData("videoTrueHdrSaturation", "-1")]
    [InlineData("videoTrueHdrSaturation", "201")]
    [InlineData("videoTrueHdrMiddleGray", "9")]
    [InlineData("videoTrueHdrMiddleGray", "101")]
    [InlineData("videoTrueHdrPeakBrightness", "1000.0")]
    [InlineData("videoTrueHdrContrast", "invalid")]
    [InlineData("videoTrueHdrSaturation", "2147483648")]
    [InlineData("videoTrueHdrMiddleGray", "")]
    public async Task GetStreamingState_InvalidTrueHdrTuning_ThrowsWithoutEnableFlag(string option, string value)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            using var state = await GetStreamingState("?" + option + "=" + value);
            _ = option switch
            {
                "videoTrueHdrPeakBrightness" => state.Request.VideoTrueHdrPeakBrightness,
                "videoTrueHdrContrast" => state.Request.VideoTrueHdrContrast,
                "videoTrueHdrSaturation" => state.Request.VideoTrueHdrSaturation,
                "videoTrueHdrMiddleGray" => state.Request.VideoTrueHdrMiddleGray,
                _ => throw new InvalidOperationException()
            };
        });
    }

    [Theory]
    [InlineData("16")]
    [InlineData("-1")]
    [InlineData("NvidiaVsr")]
    public async Task GetStreamingState_InvalidUpscalingMode_Throws(string mode)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => GetStreamingState("?videoUpscalingMode=" + mode));
    }

    private StreamInfo CreateStreamInfo()
        => new()
        {
            ItemId = _itemId,
            DeviceProfile = new DeviceProfile(),
            MediaType = DlnaProfileType.Video,
            SubProtocol = MediaStreamProtocol.hls,
            Container = "mp4",
            VideoCodecs = ["hevc"],
            AudioCodecs = ["aac"],
            Width = 3840,
            Height = 2160
        };

    private Task<StreamState> GetStreamingState(string query)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/videos/" + _itemId + "/master.m3u8";
        httpContext.Request.QueryString = new QueryString(query);
        var request = new VideoRequestDto
        {
            Id = _itemId,
            VideoCodec = "hevc",
            AudioCodec = "aac",
            Container = "mp4",
            Width = 3840,
            Height = 2160
        };

        return StreamingHelpers.GetStreamingState(
            request,
            httpContext,
            _mediaSourceManager.Object,
            Mock.Of<IUserManager>(),
            _libraryManager.Object,
            _configurationManager.Object,
            _mediaEncoder,
            _encodingHelper,
            Mock.Of<ITranscodeManager>(),
            TranscodingJobType.Hls,
            TestContext.Current.CancellationToken);
    }
}
