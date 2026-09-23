using System;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Helpers;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.Helpers
{
    public class MediaInfoHelperTests
    {
        private const string LiveStreamFilesPath = "/LiveTv/LiveStreamFiles/abc/stream.ts";

        private static MediaInfoHelper CreateHelper(
            IMediaSourceManager? mediaSourceManager = null,
            IServerApplicationHost? appHost = null,
            string baseUrl = "")
        {
            var serverConfigurationManager = new Mock<IServerConfigurationManager>();
            serverConfigurationManager
                .Setup(x => x.GetConfiguration(It.IsAny<string>()))
                .Returns(new NetworkConfiguration { BaseUrl = baseUrl });

            return new MediaInfoHelper(
                Mock.Of<IUserManager>(),
                Mock.Of<ILibraryManager>(),
                mediaSourceManager ?? Mock.Of<IMediaSourceManager>(),
                Mock.Of<IMediaEncoder>(),
                serverConfigurationManager.Object,
                Mock.Of<ILogger<MediaInfoHelper>>(),
                Mock.Of<INetworkManager>(),
                Mock.Of<IDeviceManager>(),
                appHost ?? Mock.Of<IServerApplicationHost>());
        }

        private static MediaSourceInfo CreateSource(Guid itemId, int bitrate, bool supportsDirectPlay = true)
        {
            return new MediaSourceInfo
            {
                Id = itemId.ToString("N", CultureInfo.InvariantCulture),
                Protocol = MediaProtocol.File,
                Bitrate = bitrate,
                SupportsDirectPlay = supportsDirectPlay,
                SupportsDirectStream = true,
                SupportsTranscoding = true
            };
        }

        [Theory]
        [InlineData(1920, 1080, null, 3840, 2160, 3840, 2160)]
        [InlineData(1920, 800, null, 3840, 2160, 3840, 1600)]
        [InlineData(640, 480, null, 3840, 2160, 2880, 2160)]
        [InlineData(720, 576, "4:3", 3840, 2160, 2880, 2160)]
        [InlineData(720, 480, "4:3", 3840, 2160, 2880, 2160)]
        public void GetVideoUpscalingTarget_SmallerSource_FitsBoundingBox(
            int sourceWidth,
            int sourceHeight,
            string? aspectRatio,
            int maximumWidth,
            int maximumHeight,
            int expectedWidth,
            int expectedHeight)
        {
            var mediaSource = new MediaSourceInfo
            {
                MediaStreams =
                [
                    new MediaStream
                    {
                        Type = MediaStreamType.Video,
                        Width = sourceWidth,
                        Height = sourceHeight,
                        AspectRatio = aspectRatio
                    }
                ]
            };

            var result = MediaInfoHelper.GetVideoUpscalingTarget(
                mediaSource,
                VideoUpscalingMode.Anime4KModeA,
                maximumWidth,
                maximumHeight);

            Assert.Equal(VideoUpscalingMode.Anime4KModeA, result.Mode);
            Assert.Equal(expectedWidth, result.Width);
            Assert.Equal(expectedHeight, result.Height);
        }

        [Fact]
        public void GetVideoUpscalingTarget_NativeResolution_DisablesUpscaling()
        {
            var mediaSource = new MediaSourceInfo
            {
                MediaStreams =
                [
                    new MediaStream
                    {
                        Type = MediaStreamType.Video,
                        Width = 3840,
                        Height = 2160
                    }
                ]
            };

            var result = MediaInfoHelper.GetVideoUpscalingTarget(
                mediaSource,
                VideoUpscalingMode.Fsr,
                3840,
                2160);

            Assert.Equal(VideoUpscalingMode.None, result.Mode);
            Assert.Null(result.Width);
            Assert.Null(result.Height);
        }

        [Fact]
        public void GetVideoUpscalingTarget_HdrSource_DisablesUpscaling()
        {
            var mediaSource = new MediaSourceInfo
            {
                MediaStreams =
                [
                    new MediaStream
                    {
                        Type = MediaStreamType.Video,
                        Width = 1920,
                        Height = 1080,
                        ColorTransfer = "smpte2084"
                    }
                ]
            };

            var result = MediaInfoHelper.GetVideoUpscalingTarget(
                mediaSource,
                VideoUpscalingMode.Anime4KModeA,
                3840,
                2160);

            Assert.Equal(VideoUpscalingMode.None, result.Mode);
            Assert.Null(result.Width);
            Assert.Null(result.Height);
        }

        [Theory]
        [InlineData(1920, 1080, 0, null, 3840, 2160, true)]
        [InlineData(1920, 1080, 0, "smpte2084", 3840, 2160, false)]
        [InlineData(1922, 1080, 0, null, 3840, 2160, false)]
        [InlineData(1920, 1082, 0, null, 3840, 2160, false)]
        [InlineData(1920, 1080, 90, null, 2160, 3840, false)]
        [InlineData(1080, 1920, 90, null, 3840, 2160, true)]
        [InlineData(1920, 1080, 0, null, 1920, 1080, false)]
        public void GetVideoUpscalingTarget_NvidiaVsr_UsesRotatedSourceLimits(
            int width, int height, int rotation, string? transfer, int targetWidth, int targetHeight, bool enabled)
        {
            var source = new MediaSourceInfo
            {
                MediaStreams = [new MediaStream { Type = MediaStreamType.Video, Width = width, Height = height, Rotation = rotation, ColorTransfer = transfer }]
            };
            var result = MediaInfoHelper.GetVideoUpscalingTarget(source, VideoUpscalingMode.NvidiaVsrQUltra, targetWidth, targetHeight);
            Assert.Equal(enabled ? VideoUpscalingMode.NvidiaVsrQUltra : VideoUpscalingMode.None, result.Mode);
            Assert.Equal(enabled ? targetWidth : (int?)null, result.Width);
            Assert.Equal(enabled ? targetHeight : (int?)null, result.Height);
        }

        [Theory]
        [InlineData(VideoUpscalingMode.None, 1920, 1080, null, null, null, true, true)]
        [InlineData(VideoUpscalingMode.None, 1920, 1080, null, null, null, false, false)]
        [InlineData(VideoUpscalingMode.None, 1920, 1080, null, null, "smpte2084", true, false)]
        [InlineData(VideoUpscalingMode.None, 3842, 2160, null, null, null, true, false)]
        [InlineData(VideoUpscalingMode.Fsr, 1920, 1080, 3840, 2160, null, true, false)]
        [InlineData(VideoUpscalingMode.NvidiaVsrQHigh, 1280, 720, 3840, 2160, null, true, true)]
        [InlineData(VideoUpscalingMode.NvidiaVsrLow, 1280, 720, 3840, 2160, null, true, true)]
        [InlineData(VideoUpscalingMode.NvidiaVsrUltra, 1920, 1080, 4096, 2304, null, true, false)]
        public void GetVideoTrueHdrProfile_RequiresSdrNativeGeometryAndHdr10Client(
            VideoUpscalingMode mode, int width, int height, int? outputWidth, int? outputHeight, string? transfer, bool hdrClient, bool enabled)
        {
            var source = new MediaSourceInfo
            {
                MediaStreams = [new MediaStream { Type = MediaStreamType.Video, Width = width, Height = height, ColorTransfer = transfer }]
            };
            var result = MediaInfoHelper.GetVideoTrueHdrProfile(source, CreateHdrProfile(hdrClient), mode, outputWidth, outputHeight);
            Assert.Equal(enabled, result is not null);
        }

        [Theory]
        [InlineData("ts", "hevc", "10", false)]
        [InlineData("mp4", "h264", "10", false)]
        [InlineData("mp4", "hevc", "8", false)]
        [InlineData("mp4", "hevc", "10", true)]
        public void GetVideoTrueHdrProfile_RequiresHevcTenBitFmp4(string container, string codec, string bitDepth, bool enabled)
        {
            var profile = CreateHdrProfile(true);
            profile.TranscodingProfiles[0].Container = container;
            profile.TranscodingProfiles[0].VideoCodec = codec;
            profile.CodecProfiles[0].Conditions =
            [
                new ProfileCondition { Property = ProfileConditionValue.VideoRangeType, Condition = ProfileConditionType.EqualsAny, Value = "SDR|HDR10" },
                new ProfileCondition { Property = ProfileConditionValue.VideoBitDepth, Condition = ProfileConditionType.LessThanEqual, Value = bitDepth }
            ];
            var source = new MediaSourceInfo { MediaStreams = [new MediaStream { Type = MediaStreamType.Video, Width = 1920, Height = 1080 }] };
            Assert.Equal(enabled, MediaInfoHelper.GetVideoTrueHdrProfile(source, profile, VideoUpscalingMode.None, null, null) is not null);
        }

        [Theory]
        [InlineData(true, true, false, VideoUpscalingMode.None, true)]
        [InlineData(true, true, false, VideoUpscalingMode.NvidiaVsrQHigh, true)]
        [InlineData(false, true, false, VideoUpscalingMode.None, false)]
        [InlineData(true, false, false, VideoUpscalingMode.None, false)]
        [InlineData(true, true, true, VideoUpscalingMode.None, false)]
        public void SetDeviceSpecificData_TrueHdr_NegotiatesMain10OrPreservesDirectPlay(
            bool hdrClient, bool nvenc, bool hdrSource, VideoUpscalingMode mode, bool enabled)
        {
            var user = new User("test", "auth", "reset");
            user.Permissions.Add(new Permission(PermissionKind.EnableVideoPlaybackTranscoding, true));
            var users = new Mock<IUserManager>();
            users.Setup(manager => manager.GetUserById(user.Id)).Returns(user);
            var configuration = new Mock<IServerConfigurationManager>();
            configuration.SetupGet(manager => manager.Configuration).Returns(new ServerConfiguration());
            configuration.Setup(manager => manager.GetConfiguration("encoding")).Returns(new EncodingOptions
            {
                HardwareAccelerationType = nvenc ? HardwareAccelerationType.nvenc : HardwareAccelerationType.none,
                EnableHardwareEncoding = true
            });
            var encoder = new Mock<IMediaEncoder>();
            encoder.Setup(value => value.SupportsEncoder("hevc_nvenc")).Returns(true);
            encoder.Setup(value => value.SupportsFilter("rtxvideo_cuda")).Returns(true);
            var helper = new MediaInfoHelper(users.Object, Mock.Of<ILibraryManager>(), Mock.Of<IMediaSourceManager>(), encoder.Object, configuration.Object, Mock.Of<ILogger<MediaInfoHelper>>(), Mock.Of<INetworkManager>(), Mock.Of<IDeviceManager>(), Mock.Of<IServerApplicationHost>());
            var movie = new Movie { Id = Guid.NewGuid() };
            var source = new MediaSourceInfo
            {
                Id = "source", Container = "mkv", Protocol = MediaProtocol.File, Path = "/media/test.mkv", Bitrate = 4_192_000,
                SupportsDirectPlay = true, SupportsDirectStream = true, SupportsTranscoding = true,
                MediaStreams =
                [
                    new MediaStream { Index = 0, Type = MediaStreamType.Video, Codec = "hevc", Profile = "main", Width = 1280, Height = 720, BitDepth = 8, BitRate = 4_000_000, ColorTransfer = hdrSource ? "smpte2084" : "bt709" },
                    new MediaStream { Index = 1, Type = MediaStreamType.Audio, Codec = "aac", Channels = 2, SampleRate = 48000, BitRate = 192000 }
                ]
            };
            var profile = CreateHdrProfile(hdrClient);
            var originalTranscodingProfile = profile.TranscodingProfiles[0];
            helper.SetDeviceSpecificData(movie, source, profile, new ClaimsPrincipal(), 20_192_000, 0, source.Id, null, null, null, "session", user.Id, true, true, true, true, true, mode, 3840, 2160, null, false, IPAddress.Loopback, true, 1600, 120, 90, 60);
            if (enabled)
            {
                Assert.False(source.SupportsDirectPlay);
                Assert.False(source.SupportsDirectStream);
                Assert.Equal("mp4", source.TranscodingContainer);
                Assert.Equal(MediaStreamProtocol.hls, source.TranscodingSubProtocol);
                var query = QueryHelpers.ParseQuery(new Uri("http://localhost" + source.TranscodingUrl).Query);
                Assert.Equal("hevc", query["VideoCodec"].ToString());
                Assert.Equal("main10", query["hevc-profile"].ToString());
                Assert.Equal("10", query["hevc-videobitdepth"].ToString());
                Assert.Equal("HDR10", query["hevc-rangetype"].ToString());
                Assert.Equal("true", query["videoTrueHdr"].ToString());
                Assert.Equal("1600", query["videoTrueHdrPeakBrightness"].ToString());
                Assert.Equal("false", query["allowVideoStreamCopy"].ToString());
                TestContext.Current.TestOutputHelper!.WriteLine(source.TranscodingUrl);
            }
            else
            {
                Assert.True(source.SupportsDirectPlay);
                Assert.Null(source.TranscodingUrl);
            }

            Assert.Same(originalTranscodingProfile, profile.TranscodingProfiles[0]);
        }

        private static DeviceProfile CreateHdrProfile(bool hdrClient)
        {
            return new DeviceProfile
            {
                DirectPlayProfiles = [new DirectPlayProfile { Type = DlnaProfileType.Video, Container = "mkv", VideoCodec = "hevc", AudioCodec = "aac" }],
                TranscodingProfiles = [new TranscodingProfile { Type = DlnaProfileType.Video, Context = EncodingContext.Streaming, Container = "mp4", Protocol = MediaStreamProtocol.hls, VideoCodec = "hevc", AudioCodec = "aac" }],
                CodecProfiles =
                [
                    new CodecProfile
                    {
                        Type = CodecType.Video,
                        Codec = "hevc",
                        Conditions = [new ProfileCondition { Property = ProfileConditionValue.VideoRangeType, Condition = ProfileConditionType.EqualsAny, Value = hdrClient ? "SDR|HDR10" : "SDR" }]
                    }
                ]
            };
        }

        [Fact]
        public void SortMediaSources_PreferredItemExceedsBitrate_StaysDefault()
        {
            // The version the user was watching (the queried item) must stay the default
            // even when a sibling version fits the bitrate limit better, since the resume
            // position belongs to that exact version.
            var preferredItemId = Guid.NewGuid();
            var preferredSource = CreateSource(preferredItemId, bitrate: 80_000_000, supportsDirectPlay: false);
            var siblingSource = CreateSource(Guid.NewGuid(), bitrate: 8_000_000);

            var result = new PlaybackInfoResponse
            {
                MediaSources = [siblingSource, preferredSource]
            };

            CreateHelper().SortMediaSources(result, maxBitrate: 20_000_000, preferredItemId);

            Assert.Equal(preferredSource.Id, result.MediaSources[0].Id);
        }

        [Fact]
        public void SortMediaSources_NoPreferredItem_OrdersByPlayability()
        {
            var directPlay = CreateSource(Guid.NewGuid(), bitrate: 8_000_000);
            var transcodeOnly = CreateSource(Guid.NewGuid(), bitrate: 8_000_000, supportsDirectPlay: false);
            transcodeOnly.SupportsDirectStream = false;

            var result = new PlaybackInfoResponse
            {
                MediaSources = [transcodeOnly, directPlay]
            };

            CreateHelper().SortMediaSources(result, maxBitrate: 20_000_000);

            Assert.Equal(directPlay.Id, result.MediaSources[0].Id);
        }

        [Fact]
        public void SortMediaSources_PreferredIdNotInSources_KeepsPlayabilityOrder()
        {
            var directPlay = CreateSource(Guid.NewGuid(), bitrate: 8_000_000);
            var transcodeOnly = CreateSource(Guid.NewGuid(), bitrate: 8_000_000, supportsDirectPlay: false);
            transcodeOnly.SupportsDirectStream = false;

            var result = new PlaybackInfoResponse
            {
                MediaSources = [transcodeOnly, directPlay]
            };

            CreateHelper().SortMediaSources(result, maxBitrate: 20_000_000, Guid.NewGuid());

            Assert.Equal(directPlay.Id, result.MediaSources[0].Id);
        }

        [Fact]
        public async Task GetPlaybackInfo_ExistingLiveStream_RewritesReturnedCloneOnly()
        {
            const string LocalPath = "http://172.19.0.3:8096" + LiveStreamFilesPath;

            var sharedLiveSource = new MediaSourceInfo
            {
                Id = "abc",
                Protocol = MediaProtocol.Http,
                Path = LocalPath,
                LiveStreamId = "livestream-1"
            };

            var mediaSourceManager = new Mock<IMediaSourceManager>();
            mediaSourceManager
                .Setup(x => x.GetLiveStream(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(sharedLiveSource);

            var appHost = new Mock<IServerApplicationHost>();
            appHost.Setup(x => x.GetSmartApiUrl(It.IsAny<HttpRequest>())).Returns("https://media.example.com");

            var helper = CreateHelper(mediaSourceManager: mediaSourceManager.Object, appHost: appHost.Object);

            var result = await helper.GetPlaybackInfo(new Movie(), null, Mock.Of<HttpRequest>(), liveStreamId: "live-1").ConfigureAwait(true);

            Assert.Equal("https://media.example.com" + LiveStreamFilesPath, result.MediaSources[0].Path);

            // The shared instance handed back by GetLiveStream must remain untouched; only the clone in the response may be rewritten.
            Assert.Equal(LocalPath, sharedLiveSource.Path);
        }

        [Fact]
        public async Task OpenMediaSource_RewritesReturnedLiveStreamPath()
        {
            var mediaSource = new MediaSourceInfo
            {
                Id = "abc",
                Protocol = MediaProtocol.Http,
                Path = "http://127.0.0.1:8096" + LiveStreamFilesPath,
                LiveStreamId = "livestream-1"
            };

            var helper = CreateOpenMediaSourceHelper(mediaSource, "https://public.example.com");

            var response = await helper.OpenMediaSource(new DefaultHttpContext(), new LiveStreamRequest()).ConfigureAwait(true);

            Assert.Equal("https://public.example.com" + LiveStreamFilesPath, response.MediaSource.Path);
        }

        [Fact]
        public async Task OpenMediaSource_ExternalDockerBridgeBehindReverseProxy_UsesPublishedUrl()
        {
            const string LocalPath = "http://172.23.0.5:8096" + LiveStreamFilesPath;

            // Represents the instance MediaSourceManager keeps for its own bookkeeping; the helper never sees it
            // and must not be able to affect it.
            var localSource = new MediaSourceInfo
            {
                Id = "abc",
                Protocol = MediaProtocol.Http,
                Path = LocalPath,
                LiveStreamId = "livestream-1"
            };

            var mediaSourceManager = new Mock<IMediaSourceManager>();
            mediaSourceManager
                .Setup(x => x.OpenLiveStream(It.IsAny<LiveStreamRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    // Mirrors production: MediaSourceManager.OpenLiveStream hands back its own instance, so what the
                    // helper mutates must be a deserialized copy, never localSource itself.
                    var clone = JsonSerializer.Deserialize<MediaSourceInfo>(JsonSerializer.SerializeToUtf8Bytes(localSource))!;
                    return new LiveStreamResponse(clone);
                });

            var appHost = new Mock<IServerApplicationHost>();
            appHost.Setup(x => x.GetSmartApiUrl(It.IsAny<HttpRequest>())).Returns("https://jellyfin.example.com");

            var helper = CreateHelper(mediaSourceManager: mediaSourceManager.Object, appHost: appHost.Object);

            var response = await helper.OpenMediaSource(new DefaultHttpContext(), new LiveStreamRequest()).ConfigureAwait(true);

            Assert.Equal("https://jellyfin.example.com" + LiveStreamFilesPath, response.MediaSource.Path);

            // The mock now actually derives its response from localSource, so this assertion is meaningful:
            // rewriting the returned clone must never mutate the object localSource represents.
            Assert.Equal(LocalPath, localSource.Path);
        }

        [Fact]
        public async Task OpenMediaSource_ForeignHostWithLiveStreamFilesRoute_PathUnchanged()
        {
            // A plugin or remote source can expose a path that happens to match the /LiveTv/LiveStreamFiles/
            // route shape without actually being hosted by this server. Only opened streams (which always
            // carry a LiveStreamId) are eligible for rewriting.
            const string ForeignPath = "https://other-server:8096" + LiveStreamFilesPath;

            var mediaSource = new MediaSourceInfo
            {
                Id = "abc",
                Protocol = MediaProtocol.Http,
                Path = ForeignPath
            };

            var helper = CreateOpenMediaSourceHelper(mediaSource, "https://media.example.com");

            var response = await helper.OpenMediaSource(new DefaultHttpContext(), new LiveStreamRequest()).ConfigureAwait(true);

            Assert.Equal(ForeignPath, response.MediaSource.Path);
        }

        [Theory]
        [InlineData(MediaProtocol.Http, "http://192.168.1.50:5004/live/channel1.ts")]
        [InlineData(MediaProtocol.File, "/media/livetv/buffer/abc/stream.ts")]
        [InlineData(MediaProtocol.Http, "http://172.19.0.3:8096/Videos/abc/stream.ts")]
        [InlineData(MediaProtocol.Http, "http://172.19.0.3:8096/library/movie.strm")]
        public async Task OpenMediaSource_NotAPublishableLiveStreamFilesPath_PathUnchanged(MediaProtocol protocol, string path)
        {
            var mediaSource = new MediaSourceInfo
            {
                Id = "abc",
                Protocol = protocol,
                Path = path
            };

            var helper = CreateOpenMediaSourceHelper(mediaSource, "https://media.example.com");

            var response = await helper.OpenMediaSource(new DefaultHttpContext(), new LiveStreamRequest()).ConfigureAwait(true);

            Assert.Equal(path, response.MediaSource.Path);
        }

        [Fact]
        public async Task OpenMediaSource_BaseUrlConfigured_RewritesWithBaseUrlPrefix()
        {
            var mediaSource = new MediaSourceInfo
            {
                Id = "abc",
                Protocol = MediaProtocol.Http,
                Path = "http://172.19.0.3:8096/jellyfin" + LiveStreamFilesPath,
                LiveStreamId = "livestream-1"
            };

            var helper = CreateOpenMediaSourceHelper(mediaSource, "https://media.example.com/jellyfin", "/jellyfin");

            var response = await helper.OpenMediaSource(new DefaultHttpContext(), new LiveStreamRequest()).ConfigureAwait(true);

            Assert.Equal("https://media.example.com/jellyfin" + LiveStreamFilesPath, response.MediaSource.Path);
        }

        [Fact]
        public async Task OpenMediaSource_BaseUrlSegmentMismatch_PathUnchanged()
        {
            const string LocalPath = "http://172.19.0.3:8096/jellyfin2" + LiveStreamFilesPath;

            var mediaSource = new MediaSourceInfo
            {
                Id = "abc",
                Protocol = MediaProtocol.Http,
                Path = LocalPath
            };

            var helper = CreateOpenMediaSourceHelper(mediaSource, "https://media.example.com/jellyfin", "/jellyfin");

            var response = await helper.OpenMediaSource(new DefaultHttpContext(), new LiveStreamRequest()).ConfigureAwait(true);

            Assert.Equal(LocalPath, response.MediaSource.Path);
        }

        [Fact]
        public async Task OpenMediaSource_ExplicitPortOverrideWithBaseUrl_RewritesToOverrideHostAndPort()
        {
            // Mirrors NetworkManager.GetBindAddress resolving a "internal=myhost:8097" override: the smart API
            // URL carries an explicit non-default port alongside the configured BaseUrl.
            var mediaSource = new MediaSourceInfo
            {
                Id = "abc",
                Protocol = MediaProtocol.Http,
                Path = "http://172.19.0.3:8096/jellyfin" + LiveStreamFilesPath,
                LiveStreamId = "livestream-1"
            };

            var helper = CreateOpenMediaSourceHelper(mediaSource, "http://myhost:8097/jellyfin", "/jellyfin");

            var response = await helper.OpenMediaSource(new DefaultHttpContext(), new LiveStreamRequest()).ConfigureAwait(true);

            Assert.Equal("http://myhost:8097/jellyfin" + LiveStreamFilesPath, response.MediaSource.Path);
        }

        [Fact]
        public async Task GetPlaybackInfo_TwoRequestsForSharedLiveStream_ReceiveIndependentSmartApiBases()
        {
            const string LocalPath = "http://172.19.0.3:8096" + LiveStreamFilesPath;

            // Both requests resolve the same live stream; the manager hands back its own shared instance each time.
            var sharedLiveSource = new MediaSourceInfo
            {
                Id = "abc",
                Protocol = MediaProtocol.Http,
                Path = LocalPath,
                LiveStreamId = "livestream-1"
            };

            var mediaSourceManager = new Mock<IMediaSourceManager>();
            mediaSourceManager
                .Setup(x => x.GetLiveStream(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(sharedLiveSource);

            var requestA = new DefaultHttpContext().Request;
            var requestB = new DefaultHttpContext().Request;

            var appHost = new Mock<IServerApplicationHost>();
            appHost.Setup(x => x.GetSmartApiUrl(requestA)).Returns("https://a.example.com");
            appHost.Setup(x => x.GetSmartApiUrl(requestB)).Returns("https://b.example.com");

            var helper = CreateHelper(mediaSourceManager: mediaSourceManager.Object, appHost: appHost.Object);

            var resultA = await helper.GetPlaybackInfo(new Movie(), null, requestA, liveStreamId: "live-1").ConfigureAwait(true);
            var resultB = await helper.GetPlaybackInfo(new Movie(), null, requestB, liveStreamId: "live-1").ConfigureAwait(true);

            Assert.Equal("https://a.example.com" + LiveStreamFilesPath, resultA.MediaSources[0].Path);
            Assert.Equal("https://b.example.com" + LiveStreamFilesPath, resultB.MediaSources[0].Path);

            // Neither request's rewrite may leak into the other's response or into the shared instance.
            Assert.NotEqual(resultA.MediaSources[0].Path, resultB.MediaSources[0].Path);
            Assert.Equal(LocalPath, sharedLiveSource.Path);
        }

        [Fact]
        public async Task GetPlaybackInfo_AutoOpenLiveStreamFlow_MergedOpenedSourceHasRewrittenPath()
        {
            // Reproduces MediaInfoController.GetPostedPlaybackInfo's AutoOpenLiveStream branch (~line 220-246):
            // it picks the RequiresOpening source out of GetPlaybackInfo's result, calls OpenMediaSource, then
            // merges by replacing result.MediaSources with the opened source. Building a full controller fixture
            // is impractical (it pulls in many unrelated dependencies), so this test drives the same two helper
            // calls the controller makes and asserts the merged source is the rewritten one.
            var itemId = Guid.NewGuid();
            var sourceId = itemId.ToString("N", CultureInfo.InvariantCulture);

            // The pre-open placeholder source carries a different local path than the one OpenMediaSource
            // eventually returns, so the final assertion can prove the merge picked up the freshly opened
            // source rather than the stale placeholder.
            var requiresOpeningSource = new MediaSourceInfo
            {
                Id = sourceId,
                Protocol = MediaProtocol.Http,
                Path = "http://172.19.0.3:8096/LiveTv/LiveStreamFiles/placeholder/stream.ts",
                RequiresOpening = true,
                LiveStreamId = string.Empty
            };

            var openedSource = new MediaSourceInfo
            {
                Id = sourceId,
                Protocol = MediaProtocol.Http,
                Path = "http://172.19.0.3:8096" + LiveStreamFilesPath,
                LiveStreamId = "livestream-1"
            };

            var mediaSourceManager = new Mock<IMediaSourceManager>();
            mediaSourceManager
                .Setup(x => x.GetPlaybackMediaSources(It.IsAny<BaseItem>(), It.IsAny<User>(), true, true, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { requiresOpeningSource });
            mediaSourceManager
                .Setup(x => x.OpenLiveStream(It.IsAny<LiveStreamRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    // MediaSourceManager.OpenLiveStream JSON-clones its internal MediaSourceInfo before returning
                    // it (see Emby.Server.Implementations/Library/MediaSourceManager.cs:693-706); mirror that so
                    // the in-place rewrite below can't be observed on openedSource itself.
                    var clone = JsonSerializer.Deserialize<MediaSourceInfo>(JsonSerializer.SerializeToUtf8Bytes(openedSource))!;
                    return new LiveStreamResponse(clone);
                });

            var appHost = new Mock<IServerApplicationHost>();
            appHost.Setup(x => x.GetSmartApiUrl(It.IsAny<HttpRequest>())).Returns("https://media.example.com");

            var helper = CreateHelper(mediaSourceManager: mediaSourceManager.Object, appHost: appHost.Object);

            var info = await helper.GetPlaybackInfo(new Movie(), null, Mock.Of<HttpRequest>()).ConfigureAwait(true);

            var mediaSource = info.MediaSources[0];
            Assert.True(mediaSource.RequiresOpening);
            var preOpenPath = mediaSource.Path;

            var openStreamResult = await helper.OpenMediaSource(
                new DefaultHttpContext(),
                new LiveStreamRequest { OpenToken = mediaSource.OpenToken, ItemId = itemId }).ConfigureAwait(true);

            // MediaInfoController.cs:245 - info.MediaSources = new[] { openStreamResult.MediaSource };
            info.MediaSources = new[] { openStreamResult.MediaSource };

            Assert.Equal("https://media.example.com" + LiveStreamFilesPath, info.MediaSources[0].Path);
            Assert.NotEqual(preOpenPath, info.MediaSources[0].Path);

            // The pristine OpenLiveStream response object must remain unrewritten; only the merged clone changed.
            Assert.Equal("http://172.19.0.3:8096" + LiveStreamFilesPath, openedSource.Path);
        }

        [Theory]
        [InlineData(
            "https://media.example.com",
            "http://172.19.0.3:8096" + LiveStreamFilesPath,
            MediaProtocol.Http,
            "",
            "https://media.example.com" + LiveStreamFilesPath)]
        [InlineData(
            "https://media.example.com/",
            "http://172.19.0.3:8096" + LiveStreamFilesPath + "?token=1",
            MediaProtocol.Http,
            "",
            "https://media.example.com" + LiveStreamFilesPath + "?token=1")]
        [InlineData(
            "https://media.example.com",
            "http://172.19.0.3:8096" + LiveStreamFilesPath + "#fragment",
            MediaProtocol.Http,
            "",
            "https://media.example.com" + LiveStreamFilesPath)]
        [InlineData(
            "https://media.example.com",
            "https://172.19.0.3:8920" + LiveStreamFilesPath,
            MediaProtocol.Http,
            "",
            "https://media.example.com" + LiveStreamFilesPath)]
        [InlineData(
            "https://media.example.com",
            "http://192.168.1.10:8096" + LiveStreamFilesPath,
            MediaProtocol.Http,
            "",
            "https://media.example.com" + LiveStreamFilesPath)]
        [InlineData(
            "https://media.example.com:8920",
            "http://172.19.0.3:8096" + LiveStreamFilesPath,
            MediaProtocol.Http,
            "",
            "https://media.example.com:8920" + LiveStreamFilesPath)]
        [InlineData(
            "https://media.example.com",
            "http://172.19.0.3:8096/jellyfin" + LiveStreamFilesPath,
            MediaProtocol.Http,
            "/jellyfin",
            "https://media.example.com/jellyfin" + LiveStreamFilesPath)]
        [InlineData(
            "https://jellyfin",
            "http://172.19.0.3:8096/jellyfin" + LiveStreamFilesPath,
            MediaProtocol.Http,
            "/jellyfin",
            "https://jellyfin/jellyfin" + LiveStreamFilesPath)]
        [InlineData(
            "https://media.example.com/jellyfin",
            "http://172.19.0.3:8096/jellyfin" + LiveStreamFilesPath,
            MediaProtocol.Http,
            "/jellyfin",
            "https://media.example.com/jellyfin" + LiveStreamFilesPath)]
        [InlineData(
            "https://media.example.com/jellyfin/",
            "http://172.19.0.3:8096/jellyfin" + LiveStreamFilesPath,
            MediaProtocol.Http,
            "/jellyfin",
            "https://media.example.com/jellyfin" + LiveStreamFilesPath)]
        [InlineData(
            "https://media.example.com",
            "http://172.19.0.3:8096/jellyfin2" + LiveStreamFilesPath,
            MediaProtocol.Http,
            "/jellyfin",
            null)]
        [InlineData(
            "https://media.example.com",
            "/media/livetv/buffer/abc/stream.ts",
            MediaProtocol.File,
            "",
            null)]
        [InlineData(
            "https://media.example.com",
            "not a uri",
            MediaProtocol.Http,
            "",
            null)]
        public void GetPublishedLiveStreamPath_VariousInputs_ReturnsExpected(string smartApiUrl, string localPath, MediaProtocol protocol, string baseUrl, string? expected)
        {
            var result = MediaInfoHelper.GetPublishedLiveStreamPath(smartApiUrl, localPath, protocol, baseUrl);

            Assert.Equal(expected, result);
        }

        private static MediaInfoHelper CreateOpenMediaSourceHelper(MediaSourceInfo mediaSource, string smartApiUrl, string baseUrl = "")
        {
            var mediaSourceManager = new Mock<IMediaSourceManager>();
            mediaSourceManager
                .Setup(x => x.OpenLiveStream(It.IsAny<LiveStreamRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LiveStreamResponse(mediaSource));

            var appHost = new Mock<IServerApplicationHost>();
            appHost.Setup(x => x.GetSmartApiUrl(It.IsAny<HttpRequest>())).Returns(smartApiUrl);

            return CreateHelper(mediaSourceManager: mediaSourceManager.Object, appHost: appHost.Object, baseUrl: baseUrl);
        }
    }
}
