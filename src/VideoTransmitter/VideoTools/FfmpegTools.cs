using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Text;

namespace Ugcs.Video.Tools
{
    internal static class FfmpegTools
    {
        internal static unsafe AVFrame* allocFrame(AVPixelFormat pxfmt, int width, int height)
        {
            AVFrame* f = ffmpeg.av_frame_alloc();
            if (f == null)
                throw new FfmpegException("Can't allocate frame.");

            int size = ffmpeg.avpicture_get_size(pxfmt, width, height);
            byte* buffer = (byte*)ffmpeg.av_malloc((ulong)size);
            if (buffer == null)
            {
                ffmpeg.av_free(f);
                throw new FfmpegException("Can't allocat buffer.");
            }

            f->width = width;
            f->height = height;
            f->format = (int)pxfmt;

            ffmpeg.avpicture_fill((AVPicture*)f, buffer, pxfmt, width, height);
            return f;
        }
    }
}
