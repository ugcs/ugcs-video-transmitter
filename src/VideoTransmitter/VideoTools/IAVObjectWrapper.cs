using System;
using System.Collections.Generic;
using System.Text;

namespace Ugcs.Video.Tools
{
    public interface IAVObjectWrapper
    {
        IntPtr WrappedObject { get; }
    }
}
