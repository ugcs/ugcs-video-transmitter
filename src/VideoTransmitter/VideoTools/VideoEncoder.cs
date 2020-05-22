using FFmpeg.AutoGen;
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


        /// <param name="width">Target picture width.</param>
        /// <param name="height">Target picture height.</param>
        /// <param name="pxfmt">Pixel format.</param>
        /// <param name="bitrate">The average bitrate. Null for constant quantizer encoding.</param>
        public unsafe VideoEncoder(int width, int height, AVPixelFormat pxfmt, long? bitrate)
        {
            const string CODEC = "libx264";
            AVCodec* codec = ffmpeg.avcodec_find_encoder_by_name(CODEC);
            if ((IntPtr)codec == IntPtr.Zero)
                throw new ApplicationException($"Codec '{CODEC}' not found.");

            AVCodecContext* c = ffmpeg.avcodec_alloc_context3(codec);
            if ((IntPtr)c == IntPtr.Zero)
                throw new ApplicationException("Can't allocate codec context.");

            AVPacket* pkt = ffmpeg.av_packet_alloc();
            if ((IntPtr)pkt == IntPtr.Zero)
            {
                ffmpeg.avcodec_free_context(&c);
                throw new ApplicationException("Can't allocate packet.");
            }


            if (bitrate.HasValue)
                c->bit_rate = bitrate.Value;

            c->width = width;
            c->height = height;

            // frames per second
            c->time_base = new AVRational { num = 1, den = 30 };
            c->framerate = new AVRational { num = 30, den = 1 };

            c->gop_size = 15;
            c->max_b_frames = 1;
            c->pix_fmt = pxfmt;

            ffmpeg.av_opt_set(c->priv_data, "preset", "ultrafast", 0);

            int resultCode = ffmpeg.avcodec_open2(c, codec, null);
            if (resultCode < 0)
            {
                ffmpeg.avcodec_free_context(&c);
                ffmpeg.av_packet_free(&pkt);
                throw new ApplicationException($"Could not open codec. Error code: {resultCode}");
            }

            _pkt = pkt;
            _codecContext = c;

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

                _isDisposed = true;
            }
        }

        private static unsafe void encode(AVCodecContext* enc_ctx, AVFrame* frame, AVPacket* pkt,
            Stream output)
        {
            int resultCode = ffmpeg.avcodec_send_frame(enc_ctx, frame);
            if (resultCode < 0)
                throw new ApplicationException($"Error sending a frame for encoding. Error code: {resultCode}.");

            while (resultCode >= 0)
            {
                resultCode = ffmpeg.avcodec_receive_packet(enc_ctx, pkt);
                if (resultCode == ffmpeg.AVERROR(ffmpeg.EAGAIN) || resultCode == ffmpeg.AVERROR(ffmpeg.AVERROR_EOF))
                    return;
                else if (resultCode < 0)
                    throw new ApplicationException($"Error during encoding. Error code: {resultCode}.");

                Debug.Write(String.Format("Tncoded frame %{0} (size={1})\n", pkt->pts, pkt->size));
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
