using FFmpeg.AutoGen;
using log4net;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Ugcs.Video.Tools
{
    /// <summary>
    /// Implements video encoding in a separate thread.
    /// </summary>
    public sealed class EncodingWorker : IDisposable
    {
        private const int POOL_SIZE = 10;
        private readonly ConcurrentBag<IntPtr> _framePool = new ConcurrentBag<IntPtr>();
        private readonly Thread _workingThread;
        private readonly ConcurrentQueue<IntPtr> _framesToEncode = new ConcurrentQueue<IntPtr>();
        private EncodingPipeline _pipeline;
        private bool _stopSignal = false;
        public event EventHandler<Exception> Error;
        private readonly ILog _log = LogManager.GetLogger(typeof(EncodingWorker));
        private readonly long? _bitrate;
        private FrameRateCollector _frameRateCollector;


        public Stream Output { get; set; }


        public EncodingWorker(long? bitrate)
        {
            _bitrate = bitrate;

            _workingThread = new Thread(processQueue);
            _workingThread.Name = "Encoding worker";
            _workingThread.Start();
        }

        public void Dispose()
        {
            _stopSignal = true;
        }

        public unsafe void Feed(AVFrame* frame)
        {
            if (_frameRateCollector == null)
                _frameRateCollector = new FrameRateCollector(15);

            _frameRateCollector.FrameReceived();

            IntPtr frameCopy;
            if (!_framePool.TryTake(out frameCopy))
                frameCopy = (IntPtr)allocFrame(frame);


            if (!tryCopy(frame, (AVFrame*)frameCopy, out int errorCode))
            {
                free(frameCopy);
                throw new FfmpegException($"Can't copy frame. Error code: {errorCode}");
            }

            _framesToEncode.Enqueue(frameCopy);
        }


        private unsafe bool tryCopy(AVFrame* src, AVFrame* dst, out int errorCode)
        {
            errorCode = ffmpeg.av_frame_copy(dst, src);
            if (errorCode < 0)
                return false;
            errorCode = ffmpeg.av_frame_copy_props(dst, src);
            if (errorCode < 0)
                return false;
            return true;
        }

        private unsafe AVFrame* allocFrame(AVFrame* prototype)
        {
            AVFrame* copyFrame = ffmpeg.av_frame_alloc();
            if ((IntPtr)copyFrame == IntPtr.Zero)
                throw new FfmpegException($"Can't get allocate frame. ");


            copyFrame->format = prototype->format;
            copyFrame->width = prototype->width;
            copyFrame->height = prototype->height;
            copyFrame->channels = prototype->channels;
            copyFrame->channel_layout = prototype->channel_layout;
            copyFrame->nb_samples = prototype->nb_samples;

            int rc = ffmpeg.av_frame_get_buffer(copyFrame, 32);
            if (rc < 0)
                throw new FfmpegException($"Can't get frame buffer. Error code: {rc}.");

            Debug.WriteLine("Frame allocated");
            return copyFrame;
        }

        private unsafe void processQueue()
        {
            var spin = new SpinWait();
            while (!_stopSignal)
            {
                IntPtr frame;

                if (!_framesToEncode.TryDequeue(out frame) || !_frameRateCollector.FrameRate.HasValue)
                {
                    // No frames in queue
                    spin.SpinOnce();
                    continue;
                }

                Stream output = Output;
                if (output == null)
                {
                    _log.Warn($"Trying do encode with {nameof(Output)} is null. Frame is skipped.");
                    continue;
                }


                // Lazy initialization
                if (_pipeline == null)
                {
                    try
                    {
                        _pipeline = createPipeline((AVFrame*)frame, _frameRateCollector.FrameRate.Value);
                    }
                    catch (Exception err)
                    {
                        _log.Error("Can't create pipeline.", err);
                        onError(err);
                        break;
                    }
                }

                try
                {
                    _pipeline.Encode((AVFrame*)frame, output);

                    if (_framePool.Count > POOL_SIZE)
                        free(frame);
                    else
                    {
                        _framePool.Add(frame);
                    }
                }
                catch (Exception err)
                {
                    onError(err);
                }
            }
        }

        private unsafe EncodingPipeline createPipeline(AVFrame* frame, AVRational framerate)
        {
            return new EncodingPipeline(
                              "libx264",
                              frame->width,
                              frame->height,
                              (AVPixelFormat)frame->format,
                              _bitrate,
                              framerate);
        }

        private void onError(Exception err)
        {
            Error?.Invoke(this, err);
        }

        private unsafe void free(IntPtr frame)
        {
            AVFrame* f = (AVFrame*)frame;
            ffmpeg.av_frame_free(&f);
            Debug.WriteLine("Frame disposed");
        }
    }
}
