using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Timers;

namespace Ugcs.Video.Tools
{
    /// <summary>
    /// Proxy stream to control write speed.
    /// </summary>
    public sealed class StreamWithBitrateControl: Stream, INotifyPropertyChanged
    {
        private Stream _original;
        private Timer _timer;
        private long _bytesWrited = 0;
        private long _writeSpeed = 0;
        private DateTime _started;
        private bool _isDisposed;


        public event PropertyChangedEventHandler PropertyChanged;

        
        public override bool CanRead => _original.CanRead;

        public override bool CanSeek => _original.CanSeek;

        public override bool CanWrite => _original.CanWrite;

        public override long Length => _original.Length;

        public override long Position { get => _original.Position; set => _original.Position = value; }


        /// <summary>
        /// Bytes per second.
        /// </summary>
        public long WriteSpeed
        {
            get { return _writeSpeed; }
            private set
            {
                if (_writeSpeed == value)
                    return;
                _writeSpeed = value;
                onPropertyChanged(nameof(WriteSpeed));
            }
        }


        public StreamWithBitrateControl(Stream original, TimeSpan measureInterval)
        {
            if (measureInterval.TotalMilliseconds < 500)
                throw new ArgumentOutOfRangeException(nameof(measureInterval), "Value must be greater or equeal to 500 milliseconds.");

            _original = original ?? throw new ArgumentNullException(nameof(original));
            _timer = new Timer(measureInterval.TotalMilliseconds);
            _timer.Elapsed += onTimerElapsed;
            _timer.Enabled = true;
        }
        private void onPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private void onTimerElapsed(object sender, ElapsedEventArgs e)
        {
            double bytesPerSecond = _bytesWrited * 1000 / (DateTime.Now - _started).TotalMilliseconds;
            WriteSpeed = (long)Math.Round(bytesPerSecond, MidpointRounding.AwayFromZero);
            _bytesWrited = 0;
            _started = DateTime.Now;
        }
   

        public override void Flush()
        {
            _original.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _original.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _original.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            _original.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _original.Write(buffer, offset, count);
            _bytesWrited += count;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);


            if (_isDisposed)
                return;

            _timer.Enabled = false;
            _timer.Elapsed -= onTimerElapsed;
            _timer.Dispose();
            _isDisposed = true;
        }
    }
}
