using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoTransmitter.Models
{
    public class ClientVehicle : Caliburn.Micro.PropertyChangedBase
    {
        public int VehicleId { get; set; }
        private string _name;
        public string Name
        {
            get
            {
                return _name;
            }
            set
            {
                _name = value;
                NotifyOfPropertyChange(() => Name);
            }
        }
        public string TailNumber { get; set; }
        private bool _isConnected;
        public bool IsConnected
        {
            get
            {
                return _isConnected;
            }
            set
            {
                _isConnected = value;
                NotifyOfPropertyChange(() => IsConnected);
            }
        }
    }
}
