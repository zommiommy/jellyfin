using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Controllers;
using Jellyfin.Api.Helpers;
using Jellyfin.Api.Models.MediaInfoDtos;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.MediaInfo;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.Controllers;

public class MediaInfoControllerTests
{
    private const string SourceId = "video-source";
    private readonly MediaInfoController _controller;

    public MediaInfoControllerTests()
    {
        var mediaSource = new MediaSourceInfo
        {
            Id = SourceId,
            Path = "/media/video.mkv",
            Protocol = MediaProtocol.File,
            Container = "mkv"
        };
        var mediaSourceManager = new Mock<IMediaSourceManager>();
        mediaSourceManager
            .Setup(x => x.GetPlaybackMediaSources(It.IsAny<BaseItem>(), It.IsAny<User>(), true, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { mediaSource });
        mediaSourceManager
            .Setup(x => x.OpenLiveStream(It.IsAny<LiveStreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiveStreamResponse(mediaSource));

        var libraryManager = new Mock<ILibraryManager>();
        libraryManager.Setup(x => x.GetItemById<BaseItem>(It.IsAny<Guid>(), It.IsAny<User>())).Returns(new Movie());
        var userManager = Mock.Of<IUserManager>();
        var deviceManager = Mock.Of<IDeviceManager>();
        var helper = new MediaInfoHelper(
            userManager,
            libraryManager.Object,
            mediaSourceManager.Object,
            Mock.Of<IMediaEncoder>(),
            Mock.Of<IServerConfigurationManager>(),
            NullLogger<MediaInfoHelper>.Instance,
            Mock.Of<INetworkManager>(),
            deviceManager,
            Mock.Of<IServerApplicationHost>());

        _controller = new MediaInfoController(
            mediaSourceManager.Object,
            deviceManager,
            libraryManager.Object,
            NullLogger<MediaInfoController>.Instance,
            helper,
            userManager)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    [Theory]
    [InlineData(399, null, null, null)]
    [InlineData(2001, null, null, null)]
    [InlineData(null, -1, null, null)]
    [InlineData(null, 201, null, null)]
    [InlineData(null, null, -1, null)]
    [InlineData(null, null, 201, null)]
    [InlineData(null, null, null, 9)]
    [InlineData(null, null, null, 101)]
    public async Task VideoProcessing_InvalidTrueHdrValues_ReturnBadRequestRegardlessOfEnableFlag(int? peakBrightness, int? contrast, int? saturation, int? middleGray)
    {
        foreach (var enabled in new[] { false, true })
        {
            var playbackResult = await PostPlaybackInfo(new PlaybackInfoDto
            {
                VideoTrueHdr = enabled,
                VideoTrueHdrPeakBrightness = peakBrightness,
                VideoTrueHdrContrast = contrast,
                VideoTrueHdrSaturation = saturation,
                VideoTrueHdrMiddleGray = middleGray
            });
            var liveResult = await OpenLiveStream(new OpenLiveStreamDto
            {
                VideoTrueHdr = enabled,
                VideoTrueHdrPeakBrightness = peakBrightness,
                VideoTrueHdrContrast = contrast,
                VideoTrueHdrSaturation = saturation,
                VideoTrueHdrMiddleGray = middleGray
            });

            Assert.IsType<BadRequestObjectResult>(playbackResult.Result);
            Assert.IsType<BadRequestObjectResult>(liveResult.Result);
        }
    }

    [Theory]
    [InlineData(null, null, null, null)]
    [InlineData(400, 0, 0, 10)]
    [InlineData(2000, 200, 200, 100)]
    public async Task VideoProcessing_TrueHdrBoundaries_ReturnMediaSources(int? peakBrightness, int? contrast, int? saturation, int? middleGray)
    {
        var playbackResult = await PostPlaybackInfo(new PlaybackInfoDto
        {
            VideoTrueHdr = true,
            VideoTrueHdrPeakBrightness = peakBrightness,
            VideoTrueHdrContrast = contrast,
            VideoTrueHdrSaturation = saturation,
            VideoTrueHdrMiddleGray = middleGray
        });
        var liveResult = await OpenLiveStream(new OpenLiveStreamDto
        {
            VideoTrueHdr = true,
            VideoTrueHdrPeakBrightness = peakBrightness,
            VideoTrueHdrContrast = contrast,
            VideoTrueHdrSaturation = saturation,
            VideoTrueHdrMiddleGray = middleGray
        });

        Assert.Equal(SourceId, Assert.Single(Assert.IsType<PlaybackInfoResponse>(playbackResult.Value).MediaSources).Id);
        Assert.Equal(SourceId, Assert.IsType<LiveStreamResponse>(liveResult.Value).MediaSource.Id);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(16)]
    public async Task VideoProcessing_UndefinedUpscalingMode_ReturnsBadRequest(int mode)
    {
        var playbackResult = await PostPlaybackInfo(new PlaybackInfoDto
        {
            VideoUpscalingMode = (VideoUpscalingMode)mode,
            VideoUpscalingWidth = 3840,
            VideoUpscalingHeight = 2160
        });
        var liveResult = await OpenLiveStream(new OpenLiveStreamDto
        {
            VideoUpscalingMode = (VideoUpscalingMode)mode,
            VideoUpscalingWidth = 3840,
            VideoUpscalingHeight = 2160
        });

        Assert.IsType<BadRequestObjectResult>(playbackResult.Result);
        Assert.IsType<BadRequestObjectResult>(liveResult.Result);
    }

    [Theory]
    [InlineData(VideoUpscalingMode.NvidiaVsrLow)]
    [InlineData(VideoUpscalingMode.NvidiaVsrMedium)]
    [InlineData(VideoUpscalingMode.NvidiaVsrHigh)]
    [InlineData(VideoUpscalingMode.NvidiaVsrUltra)]
    [InlineData(VideoUpscalingMode.NvidiaVsrQLow)]
    [InlineData(VideoUpscalingMode.NvidiaVsrQMedium)]
    [InlineData(VideoUpscalingMode.NvidiaVsrQHigh)]
    [InlineData(VideoUpscalingMode.NvidiaVsrQUltra)]
    public async Task VideoProcessing_RtxUpscalingMode_ReturnsMediaSources(VideoUpscalingMode mode)
    {
        var playbackResult = await PostPlaybackInfo(new PlaybackInfoDto
        {
            VideoUpscalingMode = mode,
            VideoUpscalingWidth = 3840,
            VideoUpscalingHeight = 2160
        });
        var liveResult = await OpenLiveStream(new OpenLiveStreamDto
        {
            VideoUpscalingMode = mode,
            VideoUpscalingWidth = 3840,
            VideoUpscalingHeight = 2160
        });

        Assert.Equal(SourceId, Assert.Single(Assert.IsType<PlaybackInfoResponse>(playbackResult.Value).MediaSources).Id);
        Assert.Equal(SourceId, Assert.IsType<LiveStreamResponse>(liveResult.Value).MediaSource.Id);
    }

    private Task<ActionResult<PlaybackInfoResponse>> PostPlaybackInfo(PlaybackInfoDto request)
        => _controller.GetPostedPlaybackInfo(
            itemId: Guid.Empty,
            userId: null,
            maxStreamingBitrate: null,
            startTimeTicks: null,
            audioStreamIndex: null,
            subtitleStreamIndex: null,
            maxAudioChannels: null,
            mediaSourceId: null,
            liveStreamId: null,
            autoOpenLiveStream: null,
            enableDirectPlay: null,
            enableDirectStream: null,
            enableTranscoding: null,
            allowVideoStreamCopy: null,
            allowAudioStreamCopy: null,
            playbackInfoDto: request);

    private Task<ActionResult<LiveStreamResponse>> OpenLiveStream(OpenLiveStreamDto request)
        => _controller.OpenLiveStream(
            openToken: null,
            userId: null,
            playSessionId: null,
            maxStreamingBitrate: null,
            startTimeTicks: null,
            audioStreamIndex: null,
            subtitleStreamIndex: null,
            maxAudioChannels: null,
            itemId: null,
            openLiveStreamDto: request,
            enableDirectPlay: null,
            enableDirectStream: null,
            alwaysBurnInSubtitleWhenTranscoding: null);
}
