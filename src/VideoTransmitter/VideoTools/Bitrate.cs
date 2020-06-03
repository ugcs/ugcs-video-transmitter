using System;
using System.Collections.Generic;
using System.Text;

namespace Ugcs.Video.Tools
{
    public struct Bitrate
    {
        private long _bitsPerSecond;

        public long BitsPerSecond
        {
            get
            {
                return _bitsPerSecond;
            }
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value), "The value must be greater or equal to zero.");
                _bitsPerSecond = value;
            }
        }


        public override string ToString()
        {
            long bps = BitsPerSecond;
            if (bps == 0)
                return "0";
            else if (bps > 1e6)
                return String.Format("{0:N1} Mbit/sec", bps / 1e6);
            else if (bps > 1e3)
                return String.Format("{0:N0} Kbit/sec", bps / 1e3);
            else return String.Format("{0} bit/sec", bps);
        }
    }
}
