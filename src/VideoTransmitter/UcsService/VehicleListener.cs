using System;
using System.Collections.Generic;
using System.Text;
using UcsService.DTO;
using UcsService.Enums;
using UGCS.Sdk.Protocol;
using UGCS.Sdk.Protocol.Encoding;

namespace UcsService
{
    public sealed class VehicleListener
    {
        public delegate void ObjectChangeSubscriptionCallback<T>(ModificationType modification, int id, T obj) where T : IIdentifiable;

        private EventSubscriptionWrapper _eventSubscriptionWrapper;
        private ConnectionService _connectionService;
        log4net.ILog logger = log4net.LogManager.GetLogger(typeof(VehicleListener));

        private List<SubscriptionToken> tokens = new List<SubscriptionToken>();


        private NotificationHandler _getObjectNotificationHandler<T>(ObjectChangeSubscriptionCallback<T> callback) where T : class, IIdentifiable
        {
            string invariantName = InvariantNames.GetInvariantName<T>();
            return notification =>
            {
                ObjectModificationEvent @event = notification.Event.ObjectModificationEvent;

                callback(@event.ModificationType, @event.ObjectId,
                    @event.ModificationType == ModificationType.MT_DELETE ?
                        null : (T)@event.Object.Get(invariantName));
            };
        }


        public VehicleListener(ConnectionService cs)
        {
            _connectionService = cs;
            _eventSubscriptionWrapper = new EventSubscriptionWrapper();
        }

        public void SubscribeVehicle(System.Action<ClientVehicleDTO, ModificationTypeDTO> callBack)
        {
            var subscription = new ObjectModificationSubscription();
            subscription.ObjectType = "Vehicle";

            _eventSubscriptionWrapper.ObjectModificationSubscription = subscription;

            SubscribeEventRequest requestEvent = new SubscribeEventRequest();
            requestEvent.ClientId = _connectionService.ClientId;

            requestEvent.Subscription = _eventSubscriptionWrapper;

            var responce = _connectionService.Submit<SubscribeEventResponse>(requestEvent);
            if (responce.Exception != null)
            {
                logger.Error(responce.Exception);
                throw new Exception("Failed to subscribe on vehicle modifications. Try again or see log for more details.");
            }
            var subscribeEventResponse = responce.Value;

            SubscriptionToken st = new SubscriptionToken(subscribeEventResponse.SubscriptionId, _getObjectNotificationHandler<Vehicle>(
                (token, exception, vehicle) =>
                {
                    var newCvd = new ClientVehicleDTO()
                    {
                        VehicleId = vehicle.Id,
                        Name = vehicle.Name,
                        TailNumber = vehicle.SerialNumber
                    };
                    if (token == ModificationType.MT_UPDATE || token == ModificationType.MT_CREATE)
                    {                        
                        _messageReceived(callBack, newCvd, ModificationTypeDTO.UPDATED);
                    }
                    else
                    {
                        _messageReceived(callBack, newCvd, ModificationTypeDTO.DELETED);
                    }
                }), _eventSubscriptionWrapper);
            _connectionService.NotificationListener.AddSubscription(st);
            tokens.Add(st);
        }

        public void UnsubscribeAll()
        {
            tokens.ForEach(x => _connectionService.NotificationListener.RemoveSubscription(x, out bool removedLastForId));
        }

        private void _messageReceived(System.Action<ClientVehicleDTO, ModificationTypeDTO> callback, ClientVehicleDTO vehicle, ModificationTypeDTO mtd)
        {
            callback(vehicle, mtd);
        }
    }
}
