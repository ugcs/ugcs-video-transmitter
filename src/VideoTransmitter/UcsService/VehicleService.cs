using log4net;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UcsService.DTO;
using UcsService.Enums;
using UGCS.Sdk.Protocol;
using UGCS.Sdk.Protocol.Encoding;

namespace UcsService
{
    public class VehicleService: IDisposable
    {
        private const string PREF_NAME = "mission";
        private const String VEHICLE_PATTERN = @"<selection +selection=\""(\d*)\"" *\/>";
        private const String INVARIANT_ENTITY_NAME = "Vehicle";


        private readonly VehicleListener _vehicleListener;
        public delegate void VehicleUpdated(ClientVehicleDTO vehicle);
        public event VehicleUpdated OnVehicleUpdated;
        private ILog _logger = LogManager.GetLogger(typeof(VehicleService));
        private ConnectionService _connectionService;
        private ConcurrentDictionary<int, ClientVehicleDTO> _vehicles { get; set; } = new ConcurrentDictionary<int, ClientVehicleDTO>();
        private bool _isDisposed = false;


        public bool EnableVehicleSynchronisation { get; set; }


        public VehicleService(ConnectionService cs, VehicleListener vl)
        {
            EnableVehicleSynchronisation = true;//hardcode config
            _connectionService = cs;
            cs.Connected += ucs_Connected;
            cs.Disconnected += ucs_Disconnected;

            _vehicleListener = vl;

            if (cs.IsConnected)
            {
                try
                {
                    updateAvailableVehicles();
                    _vehicleListener.SubscribeVehicle(refreshVehicle);
                }
                catch (Exception err)
                {
                    _logger.Error("Error occured.", err);
                }
            }
        }

        private void ucs_Disconnected(object sender, EventArgs e)
        {
            _vehicleListener.UnsubscribeAll();
        }

        private void ucs_Connected(object sender, EventArgs e)
        {
            try
            {
                updateAvailableVehicles();
                _vehicleListener.SubscribeVehicle(refreshVehicle);
            }
            catch (Exception err)
            {
                _logger.Error("Error occured.", err);
            }
        }
        public List<ClientVehicleDTO> GetVehicles()
        {
            List<ClientVehicleDTO> ret = new List<ClientVehicleDTO>();
            foreach (var vehicle in _vehicles)
            {
                ret.Add(new ClientVehicleDTO()
                {
                    VehicleId = vehicle.Value.VehicleId,
                    Name = vehicle.Value.Name,
                    TailNumber = vehicle.Value.TailNumber
                });
            }
            return ret;

        }
        private void updateAvailableVehicles()
        {
            ConcurrentDictionary<int, ClientVehicleDTO> res = new ConcurrentDictionary<int, ClientVehicleDTO>();
            GetObjectListRequest request = new GetObjectListRequest()
            {
                ClientId = _connectionService.ClientId,
                ObjectType = INVARIANT_ENTITY_NAME,
                RefreshDependencies = true,
            };
            request.RefreshExcludes.Add("PayloadProfile");
            request.RefreshExcludes.Add("Route");
            request.RefreshExcludes.Add("Mission");
            request.RefreshExcludes.Add("Platform");

            var response = _connectionService.Execute<GetObjectListResponse>(request);

            var returnVehicleList = new List<Vehicle>();
            foreach (var vehicles in response.Objects)
            {
                res.TryAdd(vehicles.Vehicle.Id, new ClientVehicleDTO()
                {
                    VehicleId = vehicles.Vehicle.Id,
                    Name = vehicles.Vehicle.Name,
                    TailNumber = vehicles.Vehicle.SerialNumber
                });
            }
            _vehicles = res;
        }

        /// <summary>
        /// Returns Name of vehicle with specified tail number. Can return empty is something goes wrong
        /// </summary>
        /// <param name="tailNumber"></param>
        /// <returns></returns>
        public string GetVehicleNameByTailNumber(String tailNumber)
        {
            foreach (var vehicle in _vehicles)
            {
                if (string.Equals(vehicle.Value.TailNumber, tailNumber,
                   StringComparison.OrdinalIgnoreCase))
                {
                    return vehicle.Value.Name;
                }
                //Do something with pair
            }
            return string.Empty;
        }


        private void refreshVehicle(ClientVehicleDTO vehicle, ModificationTypeDTO mtd)
        {
            bool update = false;
            if (!_vehicles.ContainsKey(vehicle.VehicleId))
            {
                update = true;
            }
            _vehicles.AddOrUpdate(vehicle.VehicleId, vehicle, (key, oldValue) =>
            {
                if (vehicle.Name != oldValue.Name)
                {
                    update = true;
                }
                return vehicle;
            });
            if (update && OnVehicleUpdated != null)
            {
                OnVehicleUpdated(vehicle);
            }
        }

        /// <summary>
        /// Create subscribtion to change the selected vehicle
        /// </summary>
        /// <param name="handler">Handler for new vehicle tail number</param>
        /// <returns>Returns subscription id or null if error is raised</returns>
        public int SubscribeSelectedVehicleChange(System.Action<string> handler)
        {
            //get selected vehicle id from mission preferences
            int result = 0;
            ObjectModificationSubscription missionPrefSubscription = new ObjectModificationSubscription();
            missionPrefSubscription.ObjectType = InvariantNames.GetInvariantName<MissionPreference>();

            EventSubscriptionWrapper subscriptionWrapper = new EventSubscriptionWrapper();
            subscriptionWrapper.ObjectModificationSubscription = missionPrefSubscription;

            var response = _connectionService.Execute<SubscribeEventResponse>(
                new SubscribeEventRequest()
                {
                    ClientId = _connectionService.ClientId,
                    Subscription = subscriptionWrapper,
                });
            result = response.SubscriptionId;
            _connectionService.NotificationListener.AddSubscription(new SubscriptionToken(response.SubscriptionId,
                (notif) =>
                {
                    var prefs = notif.Event.ObjectModificationEvent.Object.MissionPreference;
                    int? id = missionPreferenceToVehicleId(prefs, _connectionService.User.Id);
                    if (id != null)
                    {
                        string tailNum = getVehicleTailNumberById(id.Value);
                        if (tailNum != null)
                            handler(tailNum);
                    }
                },
                subscriptionWrapper));
            return response.SubscriptionId;
        }


        /// <summary>
        /// Return <see cref="null"/> if there are no info about selected vehicle
        /// </summary>
        /// <returns></returns>
        public string GetSelectedVehicleTailNumber()
        {
            if (!_connectionService.IsConnected)
                return null;

            var getMissionReq = new GetMissionPreferencesRequest()
            {
                User = _connectionService.User,
                ClientId = _connectionService.ClientId,
                Mission = null,
            };
            var getMissionResp = _connectionService.Execute<GetMissionPreferencesResponse>(getMissionReq);

            MissionPreference pref = getMissionResp.Preferences.Where(p => p.Name == PREF_NAME).FirstOrDefault();
            int? id = missionPreferenceToVehicleId(pref, getMissionReq.User.Id);
            if (id != null)
                return getVehicleTailNumberById(id.Value);

            return null;
        }


        private int? missionPreferenceToVehicleId(MissionPreference prefs, int userId)
        {
            if (prefs == null)
                return null;
            if (EnableVehicleSynchronisation &&
                prefs.Name.Equals("mission") &&
                prefs.User.Id == userId)
            {
                string selectionValue = prefs.Value;
                Regex r = new Regex(VEHICLE_PATTERN, RegexOptions.IgnoreCase);
                Match match = r.Match(selectionValue);
                if (match.Success)
                {
                    return int.Parse(match.Groups[1].Captures[0].Value);
                }
            }
            return null;
        }


        private string getVehicleTailNumberById(int id)
        {
            var getVehicleResp = _connectionService.Execute<GetObjectResponse>(
                new GetObjectRequest
                {
                    ClientId = _connectionService.ClientId,
                    ObjectType = InvariantNames.GetInvariantName<Vehicle>(),
                    ObjectId = id
                });


            var vehicle = getVehicleResp.Object?.Vehicle;
            if (vehicle != null)
                return getTailNumberFromVehicle(vehicle);
            return null;
        }

        private string getTailNumberFromVehicle(Vehicle v)
        {
            return v.SerialNumber;
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _connectionService.Connected -= ucs_Connected;
            _vehicleListener.UnsubscribeAll();

            _isDisposed = true;
        }
    }
}
