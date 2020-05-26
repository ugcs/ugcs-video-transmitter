using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Text;

namespace Ugcs.Video.Tools
{
    public sealed class Codec: IAVObjectWrapper
    {
        private readonly unsafe AVCodec* _codec;


        private unsafe Codec(AVCodec* codec)
        {
            _codec = codec;
        }


        unsafe IntPtr IAVObjectWrapper.WrappedObject => (IntPtr)_codec;


        public unsafe static Codec GetByName(string name)
        {
            AVCodec* codec = ffmpeg.avcodec_find_encoder_by_name(name);
            if (codec == null)
                throw new FfmpegException($"Codec '{name}' not found.");

            return new Codec(codec);
        }


        public unsafe bool isSupported(AVPixelFormat pxfmt)
        {
            AVCodec* codec = _codec;
            int i = 0;
            while (codec->pix_fmts[i] != AVPixelFormat.AV_PIX_FMT_NONE)
            {
                if (codec->pix_fmts[i] == pxfmt)
                {
                    return true;
                }
                i++;
            }

            return false;
        }

        public unsafe AVPixelFormat GetBestPixFmt(AVPixelFormat srcPxfmt)
        {
            int loss = 0;
            return ffmpeg.avcodec_find_best_pix_fmt_of_list(_codec->pix_fmts, srcPxfmt, 0, &loss);
        }
    }
}
