using FFmpeg.AutoGen;
using log4net;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Ugcs.Video.Tools
{
    /// <summary>
    /// Provides an easy way to encode ffmpeg frame to a stream.
    /// </summary>
    public sealed class VideoEncoder
    {
        private unsafe AVPacket* _pkt;
        private unsafe AVCodecContext* _codecContext;
        private object _disposeSyncObj = new Object();
        private bool _isDisposed;
        private static ILog _log = LogManager.GetLogger(typeof(VideoEncoder));
        private FrameConverter _scaler;


        /// <summary>
        /// Initializes video encoder for frames with fixed width, height and pixel format.
        /// </summary>
        /// <param name="srcWidth">Source picture width.</param>
        /// <param name="srcHeight">Source picture height.</param>
        /// <param name="srcPxfmt">Source зшсегку pixel format. If it is not supported by encoder
        /// then frames data will be converted to a best compatible pixel format, supported by codec.</param>
        /// <param name="bitrate">The average bitrate. Null for constant quantizer encoding.</param>
        /// <param name="framerate"></param>
        public unsafe VideoEncoder(int srcWidth, int srcHeight, AVPixelFormat srcPxfmt, long? bitrate, AVRational framerate,
            Codec codec)
        {
            AVCodec* avCodec = (AVCodec*)((IAVObjectWrapper)codec).WrappedObject;
            AVCodecContext* c = ffmpeg.avcodec_alloc_context3(avCodec);
            if ((IntPtr)c == IntPtr.Zero)
                throw new FfmpegException("Can't allocate codec context.");

            AVPacket* pkt = ffmpeg.av_packet_alloc();
            if ((IntPtr)pkt == IntPtr.Zero)
            {
                ffmpeg.avcodec_free_context(&c);
                throw new FfmpegException("Can't allocate packet.");
            }


            if (bitrate.HasValue)
                c->bit_rate = bitrate.Value;

            c->width = srcWidth;
            c->height = srcHeight;

            c->time_base = new AVRational { num = framerate.den, den = framerate.num };
            c->framerate = framerate;

            c->pix_fmt = srcPxfmt;

            const string preset = "ultrafast";
            int rc = ffmpeg.av_opt_set(c->priv_data, "preset", preset, 0);
            if (rc < 0)
                throw new FfmpegException($"Can't set codec preset '{preset}'.");

            
            const string tune = "zerolatency";
            rc = ffmpeg.av_opt_set(c->priv_data, "tune", tune, 0);
            if (rc < 0)
                throw new FfmpegException($"Can't set tune '{tune}.");

            AVDictionary* opts = null;
            int keyInt;
            const int SECONDS_BEFORE_I = 1;
            const int DEFAULT_FRAME_PER_SEC = 25;
            if (framerate.den > 0)
                keyInt = (int)(SECONDS_BEFORE_I * (double)framerate.num / framerate.den);
            else
                keyInt = SECONDS_BEFORE_I * DEFAULT_FRAME_PER_SEC;

            int resultCode = ffmpeg.av_dict_set(&opts, "x264-params", $"scenecut=0:keyint={keyInt}", 0);
            if (resultCode < 0)
            {
                ffmpeg.avcodec_free_context(&c);
                ffmpeg.av_packet_free(&pkt);
                throw new FfmpegException($"Could not prepare options dictionnary. Error code: {resultCode}");
            }

            resultCode = ffmpeg.avcodec_open2(c, avCodec, &opts);
            if (resultCode < 0)
            {
                ffmpeg.avcodec_free_context(&c);
                ffmpeg.av_packet_free(&pkt);
                throw new FfmpegException($"Could not open codec. Error code: {resultCode}");
            }

            _pkt = pkt;
            _codecContext = c;
            _log.Info($"[width: {c->width}; height: {c->height}; pxfmt: {c->pix_fmt}; " +
                $"bitrate: {c->bit_rate}] Video encoder initialized.");
        }


        public unsafe void Dispose()
        {
            lock (_disposeSyncObj)
            {
                if (_isDisposed)
                    return;

                fixed (AVCodecContext** ptr = &_codecContext)
                {
                    ffmpeg.avcodec_free_context(ptr);
                }

                fixed (AVPacket** ptr = &_pkt)
                {
                    ffmpeg.av_packet_free(ptr);
                }

                if (_scaler != null)
                    _scaler.Dispose();


                _isDisposed = true;
            }
        }

        private static unsafe void encode(AVCodecContext* enc_ctx, AVFrame* frame, AVPacket* pkt,
            Stream output)
        {
            int resultCode = ffmpeg.avcodec_send_frame(enc_ctx, frame);
            if (resultCode < 0)
                throw new FfmpegException($"Error sending a frame for encoding. Error code: {resultCode}.");

            while (resultCode >= 0)
            {
                resultCode = ffmpeg.avcodec_receive_packet(enc_ctx, pkt);
                if (resultCode == ffmpeg.AVERROR(ffmpeg.EAGAIN) || resultCode == ffmpeg.AVERROR(ffmpeg.AVERROR_EOF))
                    return;
                else if (resultCode < 0)
                    throw new FfmpegException($"Error during encoding. Error code: {resultCode}.");

                _log.DebugFormat("Encoded frame {0} (size={1}).", pkt->pts, pkt->size);
                var data = new byte[pkt->size];
                Marshal.Copy((IntPtr)pkt->data, data, 0, pkt->size);

                output.Write(data, 0, data.Length);

                ffmpeg.av_packet_unref(pkt);
            }
        }

        /// <summary>
        /// Encode the <paramref name="frame"/> and write packets to the <paramref name="output"/>.
        /// </summary>
        /// <param name="frame">Frame to encode.</param>
        /// <param name="output">Stream to write encoded packets.</param>
        public unsafe void Encode(AVFrame* frame, Stream output)
        {
            // Avoids disposing _codecContext and _pkt until frame encoding
            lock (_disposeSyncObj)
            {
                if (_isDisposed)
                    throw new ObjectDisposedException(nameof(VideoEncoder));

                encode(_codecContext, frame, _pkt, output);
            }
        }
    }
}
