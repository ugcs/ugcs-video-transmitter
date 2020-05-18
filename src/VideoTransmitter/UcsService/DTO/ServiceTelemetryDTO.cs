using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UcsService.DTO
{
    public class ServiceTelemetryDTO
    {
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public double? AltitudeAMSL { get; set; }
        public float? Heading { get; set; }
        public float? Pitch { get; set; }
        public float? Yaw { get; set; }
    }
}
