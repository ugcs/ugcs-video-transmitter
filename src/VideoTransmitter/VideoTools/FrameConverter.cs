using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Text;

namespace Ugcs.Video.Tools
{
    /// <summary>
    /// Allows to resize or convert frame pixel format.
    /// </summary>
    internal sealed class FrameConverter : IDisposable
    {
        private readonly unsafe SwsContext* _ctx;
        private readonly object _disposeSyncObject = new object();
        private readonly unsafe AVFrame* _result;
        private bool _isDisposed = false;



        public unsafe FrameConverter(int srcWidth, int srcHeight, AVPixelFormat srcFormat, int dstWidth, int dstHeight, AVPixelFormat dstFormat)
        {
            SwsContext* ctx = ffmpeg.sws_getContext(
                srcW: srcWidth,
                srcH: srcHeight,
                srcFormat: srcFormat,
                dstW: dstWidth,
                dstH: dstHeight,
                dstFormat: dstFormat,
                flags: ffmpeg.SWS_BICUBIC,
                srcFilter: null,
                dstFilter: null,
                param: null);

            if ((IntPtr)ctx == IntPtr.Zero)
                throw new FfmpegException("Cannot initialize the conversion context.");

            try
            {
                _result = FfmpegTools.allocFrame(dstFormat, dstWidth, dstHeight);
            }
            catch
            {
                ffmpeg.sws_freeContext(ctx);
                throw;
            }

            _ctx = ctx;
        }

        public FrameConverter(int srcWidth, int srcHeight, AVPixelFormat srcFormat, AVPixelFormat dstFormat)
            : this(srcWidth, srcHeight, srcFormat, srcWidth, srcHeight, dstFormat)
        {
        }


        /// <summary>
        /// Reference to the last conversion result.
        /// The object is reused each convert operation.
        /// </summary>
        public unsafe AVFrame* Result => _result;

        /// <summary>
        /// Conver source frame and wirte result frame to <see cref="Result"/>.
        /// </summary>
        /// <param name="src"></param>
        public unsafe void Convert(AVFrame* src)
        {
            lock (_disposeSyncObject)
            {
                if (_isDisposed)
                    throw new ObjectDisposedException(nameof(FrameConverter));

                ffmpeg.av_frame_copy_props(_result, src);
                int resultCode = ffmpeg.sws_scale(
                    _ctx,
                    src->data,
                    src->linesize,
                    0,
                    src->height,
                    _result->data,
                    _result->linesize);

                if (resultCode < 0)
                    throw new FfmpegException($"Could not scale the frame. Error code: {resultCode}");
            }
        }

        public unsafe void Dispose()
        {
            lock (_disposeSyncObject)
            {
                if (_isDisposed)
                    return;

                fixed (SwsContext** ptr = &_ctx)
                {
                    ffmpeg.sws_freeContext(_ctx);
                }

                fixed (AVFrame** ptr = &_result)
                {
                    ffmpeg.av_frame_free(ptr);
                }

                _isDisposed = true;
            }
        }
    }
}
