using System;
using System.Collections.Generic;
using System.Diagnostics;
using UcsService.DTO;
using UGCS.Sdk.Protocol;
using UGCS.Sdk.Protocol.Encoding;

namespace UcsService
{
    public class TelemetryListener
    {
        private const int POLLING_INTERVAL = 100;
        public delegate void TelemetryBatchSubscriptionCallback(List<VehicleTelemetry> telemetry);
        public delegate void VideoChanged(VideoSourceChangedDTO videoSource);
        private ConnectionService _connectionService;
        private EventSubscriptionWrapper _eventSubscriptionWrapper;
        private Action<int, bool> _downlinkCb;
        public event VideoChanged TelemetryVideoUrlChanged;
        private Dictionary<int, ServiceActionTelemetry> _telemetryDTOList = new Dictionary<int, ServiceActionTelemetry>();
        private object vehicleTelemetryLocker = new object();

        public TelemetryListener(ConnectionService connect)
        {
            _connectionService = connect;
            _eventSubscriptionWrapper = new EventSubscriptionWrapper();
        }
        public void SubscribeTelemtry(Action<int, bool> downlinkCallback)
        {
            _downlinkCb = downlinkCallback;
            _eventSubscriptionWrapper.TelemetryBatchSubscription = new TelemetryBatchSubscription()
            {
                PollingPeriodMilliseconds = POLLING_INTERVAL,
                PollingPeriodMillisecondsSpecified = true
            };

            SubscribeEventRequest requestEvent = new SubscribeEventRequest();
            requestEvent.ClientId = _connectionService.ClientId;

            requestEvent.Subscription = _eventSubscriptionWrapper;
            var responce = _connectionService.Submit<SubscribeEventResponse>(requestEvent);
            if (responce.Exception != null)
            {
                throw responce.Exception;
            }
            if (responce.Value == null)
            {
                throw new System.Exception("Server return empty response on SubscribeTelemtry event");
            }
            var subscribeEventResponse = responce.Value;

            SubscriptionToken st = new SubscriptionToken(subscribeEventResponse.SubscriptionId, _getTelemetryNotificationHandler(
                (telemetry) =>
                {
                    _onTelemetryBatchReceived(telemetry);
                }), _eventSubscriptionWrapper);
            _connectionService.NotificationListener.AddSubscription(st);

        }

        private NotificationHandler _getTelemetryNotificationHandler(TelemetryBatchSubscriptionCallback callback)
        {
            return notification =>
            {
                TelemetryBatchEvent @event = notification.Event.TelemetryBatchEvent;
                callback(@event.VehicleTelemetry);
            };
        }

        public ServiceTelemetryDTO GetTelemetryById(int vehicleId)
        {
            if (!_telemetryDTOList.TryGetValue(vehicleId, out ServiceActionTelemetry a))
                return null;
            
            return a.ServiceTelemetryDTO;
        }

        /// <summary>
        /// Telemetry fill
        /// </summary>
        /// <param name="vehicleId">vehicle id</param>
        /// <param name="telemetry">list with telemetry values telemetry</param>
        private void _onTelemetryBatchReceived(List<VehicleTelemetry> listOfTelemetry)
        {
            for (int k = 0; k < listOfTelemetry.Count; k++)
            {
                int vehicleId = listOfTelemetry[k].Vehicle.Id;
                if (!_telemetryDTOList.ContainsKey(vehicleId))
                {
                    _telemetryDTOList.Add(vehicleId, new ServiceActionTelemetry()
                    {
                        ServiceTelemetryDTO = new ServiceTelemetryDTO()
                    });
                }
                _telemetryReceived(vehicleId, listOfTelemetry[k].Telemetry);
            }
        }

        public static T? GetValueOrNull<T>(Value telemetryValue) where T : struct
        {
            if (telemetryValue == null) return null;

            T? returnValue = null;

            if (typeof(T) == typeof(float))
            {
                returnValue = (T)Convert.ChangeType(telemetryValue.FloatValue, typeof(T));
            }
            if (typeof(T) == typeof(long))
            {
                returnValue = (T)Convert.ChangeType(telemetryValue.LongValue, typeof(T));
            }
            if (typeof(T) == typeof(int))
            {
                returnValue = (T)Convert.ChangeType(telemetryValue.IntValue, typeof(T));
            }
            if (typeof(T) == typeof(byte))
            {
                returnValue = (T)Convert.ChangeType(telemetryValue.IntValue, typeof(T));
            }
            if (typeof(T) == typeof(bool))
            {
                returnValue = (T)Convert.ChangeType(telemetryValue.BoolValue, typeof(T));
            }
            if (typeof(T) == typeof(double))
            {
                returnValue = (T)Convert.ChangeType(telemetryValue.DoubleValue, typeof(T));
            }

            return returnValue;
        }

        public static T GetValueOrDefault<T>(Value telemetryValue) where T : struct
        {
            return GetValueOrNull<T>(telemetryValue).GetValueOrDefault();
        }



        public static T GetTelemetryValueOrDefault<T>(Value telemetryValue) where T : struct
        {
            if (telemetryValue == null)
                return default(T);

            if (telemetryValue.DoubleValueSpecified)
                return (T)Convert.ChangeType(telemetryValue.DoubleValue, typeof(T));

            if (telemetryValue.BoolValueSpecified)
                return (T)Convert.ChangeType(telemetryValue.BoolValue, typeof(T));

            if (telemetryValue.FloatValueSpecified)
                return (T)Convert.ChangeType(telemetryValue.FloatValue, typeof(T));

            if (telemetryValue.IntValueSpecified)
                return (T)Convert.ChangeType(telemetryValue.IntValue, typeof(T));

            if (telemetryValue.LongValueSpecified)
                return (T)Convert.ChangeType(telemetryValue.LongValue, typeof(T));

            return default(T);
        }

        private void _telemetryReceived(int vehicleId, List<Telemetry> telemetry)
        {
            Debug.Assert(_telemetryDTOList.ContainsKey(vehicleId), "_telemetryDTOList.ContainsKey(vehicleId)");

            ServiceTelemetryDTO vehicleTelemetry = _telemetryDTOList[vehicleId].ServiceTelemetryDTO;

            try
            {
                foreach (Telemetry t in telemetry)
                {

                    if (t.TelemetryField.Code == "downlink_present" && t.TelemetryField.Semantic == Semantic.S_BOOL && t.TelemetryField.Subsystem == Subsystem.S_FLIGHT_CONTROLLER)
                    {
                        //case TelemetryType.TT_DOWNLINK_CONNECTED:
                        var value = GetValueOrNull<bool>(t.Value).GetValueOrDefault();
                        vehicleTelemetry.DownlinkPresent = value;
                        _downlinkCb(vehicleId, value);
                    }
                    if (t.TelemetryField.Code == "altitude_amsl" && t.TelemetryField.Semantic == Semantic.S_ALTITUDE_AMSL && t.TelemetryField.Subsystem == Subsystem.S_FLIGHT_CONTROLLER)
                    {
                        //case TelemetryType.TT_MSL_ALTITUDE:
                        vehicleTelemetry.AltitudeAMSL = GetValueOrNull<float>(t.Value);
                    }
                    if (t.TelemetryField.Code == "latitude" && t.TelemetryField.Semantic == Semantic.S_LATITUDE && t.TelemetryField.Subsystem == Subsystem.S_FLIGHT_CONTROLLER)
                    {
                        //case TelemetryType.TT_LATITUDE:
                        vehicleTelemetry.Latitude = GetValueOrNull<double>(t.Value);
                    }
                    if (t.TelemetryField.Code == "longitude" && t.TelemetryField.Semantic == Semantic.S_LONGITUDE && t.TelemetryField.Subsystem == Subsystem.S_FLIGHT_CONTROLLER)
                    {
                        //case TelemetryType.TT_LONGITUDE:
                        vehicleTelemetry.Longitude = GetValueOrNull<double>(t.Value);
                    }
                    if (t.TelemetryField.Code == "heading" && t.TelemetryField.Semantic == Semantic.S_HEADING && t.TelemetryField.Subsystem == Subsystem.S_FLIGHT_CONTROLLER)
                    {
                        vehicleTelemetry.Heading = GetValueOrDefault<float>(t.Value);
                    }
                    if (t.TelemetryField.Code == "pitch" && t.TelemetryField.Semantic == Semantic.S_PITCH && t.TelemetryField.Subsystem == Subsystem.S_FLIGHT_CONTROLLER)
                    {
                        vehicleTelemetry.Pitch = GetValueOrDefault<float>(t.Value);
                    }
                    if (t.TelemetryField.Code == "roll" && t.TelemetryField.Semantic == Semantic.S_ROLL && t.TelemetryField.Subsystem == Subsystem.S_FLIGHT_CONTROLLER)
                    {
                        vehicleTelemetry.Roll = GetValueOrDefault<float>(t.Value);
                    }
                    if (t.TelemetryField.Code == "heading" && t.TelemetryField.Semantic == Semantic.S_HEADING && t.TelemetryField.Subsystem == Subsystem.S_GIMBAL)
                    {
                        vehicleTelemetry.PayloadHeading = Math.Round(GetTelemetryValueOrDefault<double>(t.Value), 10);
                    }
                    if (t.TelemetryField.Code == "pitch" && t.TelemetryField.Semantic == Semantic.S_PITCH && t.TelemetryField.Subsystem == Subsystem.S_GIMBAL)
                    {
                        vehicleTelemetry.PayloadPitch = Math.Round(GetTelemetryValueOrDefault<double>(t.Value), 10);
                    }
                    if (t.TelemetryField.Code == "roll" && t.TelemetryField.Semantic == Semantic.S_ROLL && t.TelemetryField.Subsystem == Subsystem.S_GIMBAL)
                    {
                        vehicleTelemetry.PayloadRoll = Math.Round(GetTelemetryValueOrDefault<double>(t.Value), 10);
                    }                    
                    if (t.TelemetryField.Code == "video_stream_uri" && t.TelemetryField.Semantic == Semantic.S_STRING && t.TelemetryField.Subsystem == Subsystem.S_CAMERA)
                    {
                        string oldValue = vehicleTelemetry.VideoStreamUrl;

                        string videoStreamUri;
                        if (!t.Value.TryGetAsString(out videoStreamUri))
                            videoStreamUri = null;

                        if (oldValue != videoStreamUri)
                        {
                            vehicleTelemetry.VideoStreamUrl = videoStreamUri;
                            TelemetryVideoUrlChanged?.Invoke(new VideoSourceChangedDTO()
                            {
                                VehicleId = vehicleId,
                                VideoSourceName = videoStreamUri
                            });
                        }
                    }     
                }
            }
            catch
            {
                //Silent
            }

        }

    }
}
