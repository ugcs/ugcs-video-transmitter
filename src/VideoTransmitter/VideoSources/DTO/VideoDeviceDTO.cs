using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoSources.DTO
{
    public class VideoDeviceDTO
    {
        public string Id { get; set; }
        public int VehicleId { get; set; }
        public SourceType Type { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }

        public static string GenerateId(string Id, string name)
        {
            return name + "_" + Id;
        }
    }
}
