using System;

namespace Ugcs.Video.Tools
{
    /// <summary>
    /// If <see cref="FfmpegLog"/> is enabled during error exections, then exception will contains
    /// last error message, received from ffmpeg.
    /// </summary>
    [Serializable]
    public class FfmpegException: Exception
    {

        public FfmpegException(string message)
            :base(buildMessage(message))
        {

        }

        private static string buildMessage(string message)
        {
            string lastFfmpegError = FfmpegLog.LastError;
            if (lastFfmpegError == null)
                return message;
            return $"{message} Last ffmpeg error: {lastFfmpegError}";
        }
    }
}
