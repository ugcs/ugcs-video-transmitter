using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoTransmitter.Enums
{
    public enum VideoServerStatus
    {
        NOT_READY_TO_STREAM,
        READY_TO_STREAM,
        STREAMING,
        INITIALIZING,
        FAILED
    }
}
