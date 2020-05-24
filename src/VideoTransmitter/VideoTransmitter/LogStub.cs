using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoTransmitter.Log
{
    class Logger:ILogger
    {
        private Type type;

        public Logger(Type type)
        {
            this.type = type;
        }

        public void LogInfoMessage(string v)
        {
        }
    }

    interface ILogger
    {
        void LogInfoMessage(string v);
    }
}
