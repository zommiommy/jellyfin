using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Helpers;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.Controller.Trickplay;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.Helpers;

public class DynamicHlsHelperTests
{
    [Theory]
    [InlineData("bt709", "main10")]
    [InlineData(null, "main")]
    public async Task GetMasterHlsPlaylist_TrueHdrUpscale_AdvertisesMain10ForEveryVariant(string? sourceTransfer, string requestedProfile)
    {
        var request = CreateRequest();
        request.VideoUpscalingMode = VideoUpscalingMode.NvidiaVsrQHigh;
        request.StreamOptions["videoUpscalingMode"] = nameof(VideoUpscalingMode.NvidiaVsrQHigh);
        request.StreamOptions["videoTrueHdr"] = "true";
        request.StreamOptions["hevc-profile"] = requestedProfile;
        request.StreamOptions["hevc-videobitdepth"] = "10";
        request.StreamOptions["hevc-rangetype"] = "HDR10";

        var playlist = await GetMasterPlaylist(request, sourceTransfer, "main", enableAdaptiveBitrateStreaming: true);
        var variants = GetVariants(playlist);

        Assert.Equal(3, variants.Length);
        Assert.Equal(
            "#EXT-X-STREAM-INF:BANDWIDTH=20192000,AVERAGE-BANDWIDTH=20192000,VIDEO-RANGE=PQ,CODECS=\"hvc1.2.4.L150.B0,mp4a.40.2\",RESOLUTION=3840x2160,FRAME-RATE=24",
            variants[0]);
        Assert.All(variants, variant =>
        {
            Assert.Contains(",VIDEO-RANGE=PQ", variant, StringComparison.Ordinal);
            Assert.Contains("CODECS=\"hvc1.2.4.L150.B0,mp4a.40.2\"", variant, StringComparison.Ordinal);
            Assert.Contains(",RESOLUTION=3840x2160", variant, StringComparison.Ordinal);
        });
        Assert.DoesNotContain("VIDEO-RANGE=SDR", playlist, StringComparison.Ordinal);
        foreach (var url in playlist.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(line => !line.StartsWith('#')))
        {
            var query = QueryHelpers.ParseQuery(url[(url.IndexOf('?', StringComparison.Ordinal) + 1)..]);
            Assert.Equal("true", query["videoTrueHdr"].ToString());
            Assert.Equal("hevc", query["VideoCodec"].ToString());
        }

        TestContext.Current.TestOutputHelper!.WriteLine(playlist);
    }

    [Fact]
    public async Task GetMasterHlsPlaylist_SdrTranscode_RetainsSdrMainProfile()
    {
        var request = CreateRequest();
        request.StreamOptions["hevc-profile"] = "main";

        var playlist = await GetMasterPlaylist(request, "bt709", "main");
        var variant = Assert.Single(GetVariants(playlist));

        Assert.Contains(",VIDEO-RANGE=SDR", variant, StringComparison.Ordinal);
        Assert.Contains("CODECS=\"hvc1.1.4.L150.B0,mp4a.40.2\"", variant, StringComparison.Ordinal);
        Assert.DoesNotContain("VIDEO-RANGE=PQ", playlist, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bt709", "main", "SDR", 1)]
    [InlineData("smpte2084", "main10", "PQ", 3)]
    [InlineData("arib-std-b67", "main10", "HLG", 3)]
    public async Task GetMasterHlsPlaylist_Remux_PreservesSourceRangeAndSdrFallbacks(
        string sourceTransfer,
        string sourceProfile,
        string expectedRange,
        int expectedVariants)
    {
        var request = CreateRequest();
        request.VideoCodec = "copy";
        request.Width = 1280;
        request.Height = 720;
        request.StreamOptions["hevc-profile"] = "main";

        var playlist = await GetMasterPlaylist(request, sourceTransfer, sourceProfile);
        var variants = GetVariants(playlist);

        Assert.Equal(expectedVariants, variants.Length);
        Assert.Contains(",VIDEO-RANGE=" + expectedRange, variants[0], StringComparison.Ordinal);
        Assert.Contains(
            "CODECS=\"" + HlsCodecStringHelpers.GetH265String(sourceProfile, 150) + ",mp4a.40.2\"",
            variants[0],
            StringComparison.Ordinal);
        Assert.All(variants.Skip(1), variant => Assert.Contains(",VIDEO-RANGE=SDR", variant, StringComparison.Ordinal));
        Assert.DoesNotContain("videoTrueHdr=true", playlist, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMasterHlsPlaylist_RemuxWithTrueHdrOption_DoesNotCreateSdrVariants()
    {
        var request = CreateRequest();
        request.VideoCodec = "copy";
        request.Width = 1280;
        request.Height = 720;
        request.StreamOptions["videoTrueHdr"] = "true";

        var playlist = await GetMasterPlaylist(request, "smpte2084", "main10");
        var variant = Assert.Single(GetVariants(playlist));

        Assert.Contains(",VIDEO-RANGE=PQ", variant, StringComparison.Ordinal);
        Assert.Contains("CODECS=\"hvc1.2.4.L150.B0,mp4a.40.2\"", variant, StringComparison.Ordinal);
        Assert.DoesNotContain("VIDEO-RANGE=SDR", playlist, StringComparison.Ordinal);
    }

    private static string[] GetVariants(string playlist)
        => playlist.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal))
            .Select(line => line.TrimEnd('\r'))
            .ToArray();

    private static VideoRequestDto CreateRequest()
        => new()
        {
            Id = Guid.Parse("0d2b026e-64a1-4fb1-95bc-994d9941f5be"),
            Container = "m3u8",
            SegmentContainer = "mp4",
            VideoCodec = "hevc",
            AudioCodec = "aac",
            EnableAutoStreamCopy = false,
            AllowVideoStreamCopy = false,
            AllowAudioStreamCopy = false,
            Width = 3840,
            Height = 2160,
            Framerate = 24,
            Level = "150",
            VideoBitRate = 20_000_000,
            AudioBitRate = 192_000,
            AudioChannels = 2,
            StreamOptions = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        };

    private static async Task<string> GetMasterPlaylist(
        VideoRequestDto request,
        string? sourceTransfer,
        string sourceProfile,
        bool enableAdaptiveBitrateStreaming = false)
    {
        var mediaSource = new MediaSourceInfo
        {
            Id = request.Id.ToString("N", CultureInfo.InvariantCulture),
            Path = "/media/source.mkv",
            Container = "mkv",
            Protocol = MediaProtocol.File,
            RunTimeTicks = TimeSpan.FromMinutes(1).Ticks,
            MediaStreams =
            [
                new MediaStream
                {
                    Index = 0,
                    Type = MediaStreamType.Video,
                    Codec = "hevc",
                    Profile = sourceProfile,
                    Level = 150,
                    Width = 1280,
                    Height = 720,
                    RealFrameRate = 24,
                    AverageFrameRate = 24,
                    BitRate = 20_000_000,
                    ColorTransfer = sourceTransfer
                },
                new MediaStream
                {
                    Index = 1,
                    Type = MediaStreamType.Audio,
                    Codec = "aac",
                    Profile = "LC",
                    Channels = 2,
                    SampleRate = 48000,
                    BitRate = 192_000
                }
            ]
        };
        var item = new Movie { Id = request.Id };
        var libraryManager = new Mock<ILibraryManager>();
        libraryManager.Setup(manager => manager.GetItemById<BaseItem>(request.Id)).Returns(item);
        var mediaSourceManager = new Mock<IMediaSourceManager>();
        mediaSourceManager.Setup(manager => manager.GetPlaybackMediaSources(item, null!, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { mediaSource });
        var options = new EncodingOptions
        {
            AllowHevcEncoding = true,
            TranscodingTempPath = Path.GetTempPath()
        };
        var appPaths = Mock.Of<IApplicationPaths>();
        var configurationManager = new Mock<IServerConfigurationManager>();
        configurationManager.Setup(manager => manager.GetConfiguration("encoding")).Returns(options);
        configurationManager.SetupGet(manager => manager.CommonApplicationPaths).Returns(appPaths);
        var mediaEncoder = new Mock<IMediaEncoder>();
        mediaEncoder.Setup(encoder => encoder.CanEncodeToAudioCodec("aac")).Returns(true);
        var encodingHelper = new EncodingHelper(
            appPaths,
            mediaEncoder.Object,
            Mock.Of<ISubtitleEncoder>(),
            Mock.Of<IConfiguration>(),
            configurationManager.Object,
            Mock.Of<IPathManager>());
        var query = new Dictionary<string, string?>(request.StreamOptions, StringComparer.OrdinalIgnoreCase)
        {
            ["VideoCodec"] = request.VideoCodec,
            ["AudioCodec"] = request.AudioCodec,
            ["SegmentContainer"] = request.SegmentContainer,
            ["Width"] = request.Width?.ToString(CultureInfo.InvariantCulture),
            ["Height"] = request.Height?.ToString(CultureInfo.InvariantCulture),
            ["Framerate"] = request.Framerate?.ToString(CultureInfo.InvariantCulture),
            ["Level"] = request.Level,
            ["VideoBitrate"] = request.VideoBitRate?.ToString(CultureInfo.InvariantCulture),
            ["AudioBitrate"] = request.AudioBitRate?.ToString(CultureInfo.InvariantCulture),
            ["AllowVideoStreamCopy"] = "false",
            ["AllowAudioStreamCopy"] = "false"
        };
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "GET";
        httpContext.Request.Path = "/Videos/" + mediaSource.Id + "/master.m3u8";
        httpContext.Request.QueryString = new QueryString(QueryHelpers.AddQueryString(string.Empty, query));
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.1");
        var helper = new DynamicHlsHelper(
            libraryManager.Object,
            Mock.Of<IUserManager>(),
            mediaSourceManager.Object,
            configurationManager.Object,
            mediaEncoder.Object,
            Mock.Of<ITranscodeManager>(),
            Mock.Of<INetworkManager>(),
            NullLogger<DynamicHlsHelper>.Instance,
            new HttpContextAccessor { HttpContext = httpContext },
            encodingHelper,
            Mock.Of<ITrickplayManager>());

        var result = await helper.GetMasterHlsPlaylist(TranscodingJobType.Hls, request, enableAdaptiveBitrateStreaming);
        return Encoding.UTF8.GetString(Assert.IsType<FileContentResult>(result).FileContents);
    }
}
