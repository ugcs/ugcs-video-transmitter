using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UGCS.Sdk.Protocol.Encoding;

namespace UcsService
{
    public static class TelemetryUtils
    {
        /// <param name="v">The value of a certain telemetry field.</param>
        /// <returns>Returns <see cref="Value.StringValue"/> if it is specified.
        /// Otherwise (also if <paramref name="v"/> is null) returns null.</returns>
        public static bool TryGetAsString(this Value v, out string result)
        {
            if (v == null || !v.StringValueSpecified)
            {
                result = null;
                return false;
            }

            result = v.StringValue;
            return true;
        }
    }
}
