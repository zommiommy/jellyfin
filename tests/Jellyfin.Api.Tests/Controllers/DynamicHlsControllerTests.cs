using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Controllers;
using Jellyfin.MediaEncoding.Hls.Playlist;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.Controllers
{
    public class DynamicHlsControllerTests
    {
        [Theory]
        [MemberData(nameof(GetSegmentLengths_Success_TestData))]
        public void GetSegmentLengths_Success(long runtimeTicks, int segmentlength, double[] expected)
        {
            var res = DynamicHlsController.GetSegmentLengthsInternal(runtimeTicks, segmentlength);
            Assert.Equal(expected.Length, res.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], res[i]);
            }
        }

        public static TheoryData<long, int, double[]> GetSegmentLengths_Success_TestData()
        {
            var data = new TheoryData<long, int, double[]>();
            data.Add(0, 6, Array.Empty<double>());
            data.Add(
                TimeSpan.FromSeconds(3).Ticks,
                6,
                new double[] { 3 });
            data.Add(
                TimeSpan.FromSeconds(6).Ticks,
                6,
                new double[] { 6 });
            data.Add(
                TimeSpan.FromSeconds(3.3333333).Ticks,
                6,
                new double[] { 3.3333333 });
            data.Add(
                TimeSpan.FromSeconds(9.3333333).Ticks,
                6,
                new double[] { 6, 3.3333333 });

            return data;
        }

        [Fact]
        public async Task WaitForActiveTranscodingRequests_WaitsUntilRequestCompletes()
        {
            var job = new TranscodingJob(NullLogger<TranscodingJob>.Instance)
            {
                ActiveRequestCount = 1
            };

            var waitTask = DynamicHlsController.WaitForActiveTranscodingRequests(job, CancellationToken.None);
            Assert.False(waitTask.IsCompleted);

            job.DecrementActiveRequestCount();

            await waitTask;
        }

        [Fact]
        public async Task WaitForActiveTranscodingRequests_WaitsForEveryRequest()
        {
            var job = new TranscodingJob(NullLogger<TranscodingJob>.Instance)
            {
                ActiveRequestCount = 2
            };

            var waitTask = DynamicHlsController.WaitForActiveTranscodingRequests(job, CancellationToken.None);
            job.DecrementActiveRequestCount();

            await Task.Delay(150, TestContext.Current.CancellationToken);
            Assert.False(waitTask.IsCompleted);

            job.DecrementActiveRequestCount();

            await waitTask;
        }

        [Fact]
        public async Task WaitForActiveTranscodingRequests_ReturnsWithoutAnActiveRequest()
        {
            var job = new TranscodingJob(NullLogger<TranscodingJob>.Instance);

            await DynamicHlsController.WaitForActiveTranscodingRequests(job, CancellationToken.None);
            await DynamicHlsController.WaitForActiveTranscodingRequests(null, CancellationToken.None);
        }

        [Fact]
        public async Task WaitForActiveTranscodingRequests_ObservesCancellation()
        {
            var job = new TranscodingJob(NullLogger<TranscodingJob>.Instance)
            {
                ActiveRequestCount = 1
            };
            using var cancellationTokenSource = new CancellationTokenSource();

            var waitTask = DynamicHlsController.WaitForActiveTranscodingRequests(job, cancellationTokenSource.Token);
            await cancellationTokenSource.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitTask);
        }

        [Fact]
        public async Task ActiveRequestCount_UpdatesAtomically()
        {
            const int RequestCount = 1000;
            var job = new TranscodingJob(NullLogger<TranscodingJob>.Instance);

            await Task.WhenAll(
                Task.Run(() => Parallel.For(0, RequestCount, _ => job.IncrementActiveRequestCount())),
                Task.Run(() => Parallel.For(0, RequestCount, _ => job.DecrementActiveRequestCount())));

            Assert.Equal(0, job.ActiveRequestCount);
        }

        [Fact]
        public void GetVideoArguments_TrueHdr_EncodesMain10WithExplicitHdrColorAndHvc1Tags()
        {
            var modelDirectory = Directory.CreateTempSubdirectory("jellyfin-hls-truehdr-test-");
            try
            {
                File.WriteAllBytes(Path.Combine(modelDirectory.FullName, "truehdr.zip"), Array.Empty<byte>());
                File.WriteAllBytes(Path.Combine(modelDirectory.FullName, "vsr.zip"), Array.Empty<byte>());
                var controller = CreateController(modelDirectory.FullName);
                using var state = CreateVideoState(trueHdr: true, streamCopy: false);
                state.BaseRequest.VideoUpscalingMode = VideoUpscalingMode.NvidiaVsrQHigh;
                state.BaseRequest.Width = 3840;
                state.BaseRequest.Height = 2160;

                var arguments = controller.GetVideoArguments(state, 0, false, "mp4");

                Assert.Contains("-codec:v:0 hevc_nvenc", arguments, StringComparison.Ordinal);
                Assert.Contains(" -profile:v:0 main10", arguments, StringComparison.Ordinal);
                Assert.Contains(" -tag:v:0 hvc1", arguments, StringComparison.Ordinal);
                Assert.Contains(" -color_primaries bt2020 -color_trc smpte2084 -colorspace bt2020nc -color_range tv", arguments, StringComparison.Ordinal);
                Assert.Contains("format=p010", arguments, StringComparison.Ordinal);
                TestContext.Current.TestOutputHelper!.WriteLine(arguments.Replace(modelDirectory.FullName, "/models", StringComparison.Ordinal));
            }
            finally
            {
                modelDirectory.Delete(true);
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        public void GetVideoArguments_WithoutTrueHdrEncoding_DoesNotOverrideOutputColor(bool trueHdr, bool streamCopy)
        {
            var controller = CreateController();
            using var state = CreateVideoState(trueHdr, streamCopy);

            var arguments = controller.GetVideoArguments(state, 0, false, "mp4");

            Assert.Contains("-codec:v:0 " + (streamCopy ? "copy" : "hevc_nvenc"), arguments, StringComparison.Ordinal);
            Assert.Contains(" -tag:v:0 hvc1", arguments, StringComparison.Ordinal);
            Assert.DoesNotContain(" -color_", arguments, StringComparison.Ordinal);
            Assert.DoesNotContain(" -colorspace ", arguments, StringComparison.Ordinal);
        }

        [Fact]
        public void GetVideoArguments_TrueHdr_RejectsStreamCopy()
        {
            using var state = CreateVideoState(trueHdr: true, streamCopy: true);
            Assert.Throws<NotSupportedException>(() => CreateController().GetVideoArguments(state, 0, false, "mp4"));
        }

        private static StreamState CreateVideoState(bool trueHdr, bool streamCopy)
        {
            var video = new MediaStream
            {
                Index = 0,
                Type = MediaStreamType.Video,
                Codec = "hevc",
                Profile = streamCopy ? "main10" : "main",
                PixelFormat = streamCopy ? "yuv420p10le" : "yuv420p",
                BitDepth = streamCopy ? 10 : 8,
                Width = 1280,
                Height = 720,
                RealFrameRate = 24,
                AverageFrameRate = 24,
                ColorTransfer = streamCopy ? "smpte2084" : "bt709"
            };
            var request = new VideoRequestDto
            {
                VideoCodec = "hevc",
                Profile = "main",
                Level = "150",
                SegmentContainer = "mp4",
                VideoBitRate = 20_000_000
            };
            request.StreamOptions["videoTrueHdr"] = trueHdr ? "true" : "false";

            return new StreamState(Mock.Of<IMediaSourceManager>(), TranscodingJobType.Hls, Mock.Of<ITranscodeManager>())
            {
                Request = request,
                IsInputVideo = true,
                VideoType = VideoType.VideoFile,
                VideoStream = video,
                MediaSource = new MediaSourceInfo { Container = "mkv", MediaStreams = [video] },
                OutputVideoCodec = streamCopy ? "copy" : "hevc",
                OutputVideoBitrate = 20_000_000,
                OutputContainer = "mp4"
            };
        }

        private static DynamicHlsController CreateController(string modelDirectory = "")
        {
            var options = new EncodingOptions
            {
                HardwareAccelerationType = HardwareAccelerationType.nvenc,
                EnableHardwareEncoding = true,
                NvidiaRtxVideoModelDirectory = modelDirectory
            };
            var configurationManager = new Mock<IServerConfigurationManager>();
            configurationManager.Setup(manager => manager.GetConfiguration("encoding")).Returns(options);
            var mediaEncoder = new Mock<IMediaEncoder>();
            mediaEncoder.SetupGet(encoder => encoder.EncoderVersion).Returns(new Version(7, 1));
            mediaEncoder.Setup(encoder => encoder.SupportsEncoder("hevc_nvenc")).Returns(true);
            mediaEncoder.Setup(encoder => encoder.SupportsFilter("rtxvideo_cuda")).Returns(true);
            mediaEncoder.Setup(encoder => encoder.SupportsFilter("hwupload_cuda")).Returns(true);
            mediaEncoder.Setup(encoder => encoder.EscapeSubtitleFilterPath(It.IsAny<string>())).Returns((string path) => path);
            var encodingHelper = new EncodingHelper(
                Mock.Of<IApplicationPaths>(),
                mediaEncoder.Object,
                Mock.Of<ISubtitleEncoder>(),
                Mock.Of<IConfiguration>(),
                configurationManager.Object,
                Mock.Of<IPathManager>());

            return new DynamicHlsController(
                Mock.Of<ILibraryManager>(),
                Mock.Of<IUserManager>(),
                Mock.Of<IMediaSourceManager>(),
                configurationManager.Object,
                mediaEncoder.Object,
                Mock.Of<IFileSystem>(),
                Mock.Of<ITranscodeManager>(),
                NullLogger<DynamicHlsController>.Instance,
                null!,
                encodingHelper,
                Mock.Of<IDynamicHlsPlaylistGenerator>());
        }
    }
}
