using System;
using System.Collections.Generic;
using System.Text;

namespace Ugcs.Video.Tools
{
    public class Log4netTraceListener : System.Diagnostics.TraceListener
    {
        private readonly log4net.ILog _log;


        public Log4netTraceListener(string typeName)
            :this(Type.GetType(typeName))
        {
        }

        public Log4netTraceListener(Type logSource)
        {
            if (logSource == null)
                throw new ArgumentNullException(nameof(logSource));

            _log = log4net.LogManager.GetLogger(logSource);
        }

        public Log4netTraceListener(log4net.ILog log)
        {
            _log = log;
        }

        public override void Write(string message)
        {
            if (_log != null)
            {
                _log.Debug(message);
            }
        }

        public override void WriteLine(string message)
        {
            if (_log != null)
            {
                _log.Debug(message);
            }
        }
    }
}
