using FFmpeg.AutoGen;
using log4net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Ugcs.Video.Tools
{
    public sealed class EncodingPipeline: IDisposable
    {
        private readonly ILog _log = LogManager.GetLogger(typeof(EncodingPipeline));
        private readonly FrameConverter _converter;
        private readonly VideoEncoder _encoder;
        private readonly object _disposeSync = new object();
        private bool _isDisposed = false;

        
        public EncodingPipeline(string codecName, int srcWidth, int srcHeight, AVPixelFormat srcPxfmt, 
            long? bitrate, AVRational framerate)
        {
            var codec = Codec.GetByName(codecName);

            AVPixelFormat dstPixelFormat;
            if (codec.isSupported(srcPxfmt))
            {
                dstPixelFormat = srcPxfmt;
            }
            else
            {
                dstPixelFormat = codec.GetBestPixFmt(srcPxfmt);
                _log.Info($"[{srcPxfmt}] Pixel format is not supported. The best target format, supported by codec, is '{dstPixelFormat}'.");
                _converter = new FrameConverter(srcWidth, srcHeight, srcPxfmt, dstPixelFormat);
            }

            try
            {
                _encoder = new VideoEncoder(srcWidth, srcHeight, dstPixelFormat, bitrate, framerate, codec);
            }
            catch
            {
                if (_converter != null)
                    _converter.Dispose();
                throw;
            }
        }


        public unsafe void Encode(AVFrame* frame, Stream output)
        {
            lock (_disposeSync)
            {
                if (_isDisposed)
                    throw new ObjectDisposedException(nameof(EncodingPipeline));

                AVFrame* frameToEncode = frame;
                if (_converter != null)
                {
                    _converter.Convert(frame);
                    frameToEncode = _converter.Result;
                }
                _encoder.Encode(frameToEncode, output);
            }
        }

        public void Dispose()
        {
            lock (_disposeSync)
            {
                if (_isDisposed)
                    return;

                _encoder.Dispose();

                if (_converter != null)
                    _converter.Dispose();

                _isDisposed = true;
            }
        }
    }
}
