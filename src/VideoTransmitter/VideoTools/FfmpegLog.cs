using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Ugcs.Video.Tools
{
    public static class FfmpegLog
    {
        public class LogEventArgs: EventArgs
        {
            public LogEventArgs(string message, int level)
            {
                Message = message;
                Level = level;
            }


            public string Message { get; private set; }

            /// <summary>
            /// See ffmpeg.AV_LOG_*.
            /// </summary>
            public int Level { get; private set; }
        }


        private static av_log_set_callback_callback _logCallback;
        private static int? _level;


        public static event EventHandler<LogEventArgs> MessageReceived;
        internal static string LastError { get; private set; } = null;


        /// <summary>
        /// 
        /// Null - no log.
        /// </summary>
        public static int? Level
        {
            get { return _level; }
            set
            {
                
            }
        }

        /// <summary>
        /// Enable logging from ffmpeg with specified levels.
        /// Executing mltiple times with the same level do nothing.
        /// </summary>
        /// <param name="level">Log level of ffmpeg. See ffmpeg.AV_LOG_*.</param>
        public static void Enable(int level)
        {
            if (_level == level)
                return;

            _level = level;
                InitFfmpegLog(_level.Value);
        }

        public static void Disable()
        {
            // TODO: implement unsubscribubg from ffmpeg
        }

        private static unsafe void InitFfmpegLog(int level)
        {
            ffmpeg.av_log_set_level(level);
            _logCallback = (p0, l, format, vl) =>
            {
                if (l > ffmpeg.av_log_get_level()) return;

                var lineSize = 1024;
                var lineBuffer = stackalloc byte[lineSize];
                var printPrefix = 1;
                ffmpeg.av_log_format_line(p0, l, format, vl, lineBuffer, lineSize, &printPrefix);
                var line = Marshal.PtrToStringAnsi((IntPtr)lineBuffer);

                if (l == ffmpeg.AV_LOG_ERROR)
                    LastError = line;
                MessageReceived?.Invoke(null, new LogEventArgs(line, l));
            };
            ffmpeg.av_log_set_callback(_logCallback);
        }
    }
}
