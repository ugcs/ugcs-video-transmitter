using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Ugcs.Video.Tools
{
    /// <summary>
    /// Collects average framerate for frame sequence.
    /// </summary>
    /// <remarks>Not thread safe.</remarks>
    public sealed class FrameRateCollector
    {
        private Stopwatch _stopwatch = new Stopwatch();
        private int _framesReceived;
        private int _framesToCollect;


        /// <param name="framesToCollect">The number of frames that must be collected to calculate average frame rate.
        /// The value must be greater then 1.</param>
        public FrameRateCollector(int framesToCollect)
        {
            if (framesToCollect < 2)
                throw new ArgumentOutOfRangeException("The value must be greater then 1.");

            _framesToCollect = framesToCollect;
        }

        public AVRational? FrameRate { get; private set; }

        /// <summary>
        /// Call this method each time when frame is received.
        /// </summary>
        public void FrameReceived()
        {
            if (_framesReceived % _framesToCollect == 0)
            {
                if (_framesReceived != 0)
                {
                    if (_stopwatch.Elapsed.TotalSeconds > 0)
                        FrameRate = new AVRational { num = _framesReceived * 1000, den = (int)_stopwatch.Elapsed.TotalMilliseconds };
                    else
                        FrameRate = new AVRational { num = _framesReceived, den = 1 };
                    Debug.WriteLine($"{nameof(FrameRateCollector)}: Framerate: {FrameRate.Value.num}/{FrameRate.Value.den}");
                }

                _stopwatch.Restart();
                _framesReceived = 0;
            }

            _framesReceived++;
        }
    }
}
