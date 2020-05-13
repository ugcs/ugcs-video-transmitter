using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UcsService
{
    public class ConnectionException : Exception
    {
        public ConnectionException(string message)
            : base(message)
        {

        }

        public ConnectionException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public ConnectionException(Exception innerException)
            : base("Connection failure.", innerException)
        {
        }
    }
}
