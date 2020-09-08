using ProtoBuf;
using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UcsService;
using UGCS.Sdk.Protocol;
using UGCS.Sdk.Protocol.Encoding;
using UGCS.Sdk.Tasks;

namespace UcsService
{
    public sealed class ConnectionService : IDisposable
    {
        private const int NOTIFICATION_LISTENER_ID = -1;
        private const int LICENSE_CONSTRAINT_ERROR = 1001;
        private const String VERSION_PATTERN = @"(version \<b\>(?<version>[^\,\'\<]+)\</b\>)";


        private User _user;
        private TcpClient _tcpClient;
        private bool _disposed = false;
        private int? _clientId = null;
        private MessageExecutor _messageExecutor;
        private log4net.ILog _log = log4net.LogManager.GetLogger(typeof(ConnectionService));
        private object _connectSync = new object();


        public event EventHandler Disconnected;
        public event EventHandler Connected;


        public NotificationListener NotificationListener { get; }

        /// <summary>
        /// Returns current client id for current connection session.
        /// If connection is not established then <see cref="InvalidOperationException"/> is thrown.
        /// </summary>
        public int ClientId
        {
            get
            {
                int? clientId = _clientId;
                if (clientId == null)
                    throw newConnectionNotEstablisherException();
                return clientId.Value;
            }
        }

        /// <summary>
        /// Returns currently authorized user.
        /// If connection is not established then <see cref="InvalidOperationException"/> is thrown.
        /// </summary>
        public User User
        {
            get
            {
                User user = _user;
                if (user == null)
                    throw newConnectionNotEstablisherException();
                return user;
            }
        }


        public bool IsConnected { get => _tcpClient.Session?.IsConnected ?? false; }

        public ConnectionService()
        {
            NotificationListener = new NotificationListener();
            _tcpClient = new TcpClient();
        }

        public MessageFuture<T> Submit<T>(
                IExtensible message,
                Action<FutureResult> callback = null,
                Action<OperationStatus> statusCallback = null,
                InputStreamCallback inputStreamCallback = null)
            where T : IExtensible
        {
            if (!IsConnected)
                throw new InvalidOperationException("Connection with ucs not established.");
            return _messageExecutor.Submit<T>(message, callback, statusCallback, inputStreamCallback);
        }



        public void Dispose()
        {
            if (_disposed)
                return;

            if (IsConnected)
            {
                try
                {
                    Disconnect();
                }
                catch (Exception e)
                {
                    // supress exceptions in Dispose
                    _log.Error($"Error while disposing an instance of {nameof(ConnectionService)}.", e);
                }
            }

            var session = _tcpClient?.Session;
            if (session != null)
                session.Disconnected -= tcpClientSession_onDisconnected;

            NotificationListener?.Dispose();

            _disposed = true;
        }

        /// <summary>
        /// Connect to UgCS server.
        /// If already connected then <see cref="InvalidOperationException" /> is thrown.
        /// </summary>
        /// <param name="login"></param>
        /// <param name="password"></param>
        /// <param name="host"></param>
        /// <param name="port"></param>
        /// <param name="exception"></param>
        public void Connect(Uri address, UcsCredentials credentials)
        {
            if (credentials == null)
                throw new ArgumentNullException(nameof(credentials));
            if (address == null)
                throw new ArgumentNullException(nameof(address));
            if (!address.IsAbsoluteUri)
                throw new ArgumentException("The value must be an absolute uri.", nameof(address));
            if (!String.Equals(address.Scheme, "tcp", StringComparison.InvariantCultureIgnoreCase))
                throw new ArgumentException($"[value: '{address}'] Only 'tcp' scheme is supported.", nameof(address));

            _log.DebugFormat("Connecting to ucs at '{0}'...", address);

            lock (_connectSync)
            {
                if (IsConnected)
                    throw new InvalidOperationException("Already connected.");


                Debug.Assert(_messageExecutor == null, "_messageExecutor == null");

                try
                {
                    TcpClientSession session = openConnection(_tcpClient, address); ;
                    session.Disconnected += tcpClientSession_onDisconnected;

                    var executor = new MessageExecutor(session, new InstantTaskScheduler());
                    executor.Configuration.DefaultTimeout = 30000;
                    executor.Receiver.AddListener(NOTIFICATION_LISTENER_ID, NotificationListener);
                    _messageExecutor = executor;

                    int clientId = authenticateApp();
                    User user = authenticateUser(clientId, credentials);

                    _clientId = clientId;
                    _user = user;
                }
                catch (Exception e)
                {
                    if (_log.IsDebugEnabled)
                        _log.Debug($"[address: {address}] Connection failed.", e);
                    _tcpClient.Close();
                    releaseState();
                    throw;
                }
            }

            _log.DebugFormat("Connection established at '{0}'.", address);
            Connected?.Invoke(this, EventArgs.Empty);
        }

        public Task ConnectAsync(Uri address, UcsCredentials credentials)
        {
            // TODO: Implement really async connection
            return Task.Run(() => Connect(address, credentials));
        }

        private static TcpClientSession openConnection(TcpClient client, Uri address)
        {
            client.Connect(address.Host, address.Port);
            return client.Session;
        }

        public TResponse Execute<TResponse>(IExtensible request)
            where TResponse : IExtensible
        {
            var execution = _messageExecutor.Submit<TResponse>(request);
            execution.Wait();

            if (execution.Exception != null)
                throw execution.Exception;

            if (execution.Exception == null && execution.Value == null)
                throw new ApplicationException("Server returned an empty response and no errors, that's unexpected.");

            return execution.Value;
        }

        /// <summary>Returns client id, assigned by server.</summary>
        private int authenticateApp()
        {
            try
            {
                return
                    Execute<AuthorizeHciResponse>(
                        new AuthorizeHciRequest()
                        {
                            ClientId = -1,
                            ClientVersion = new ProtocolVersion()
                            {
                                Major = 1,
                                Minor = 2
                            }
                        })
                        .ClientId;
            }
            catch (ServerException err)
            {
                // TODO: Exception messages from server contains tags.
                if (isLicenseError(err))
                    throw new ConnectionException("Licence does not allow remote connections.", err);

                throw new ConnectionException(err);
            }
        }

        /// <summary>Returns user, received from server.</summary>
        private User authenticateUser(int clientId, UcsCredentials credentials)
        {
            LoginResponse auth;
            try
            {
                auth = Execute<LoginResponse>(
                    new LoginRequest()
                    {
                        ClientId = clientId,
                        UserLogin = credentials.Login,
                        UserPassword = credentials.Password,
                    });
            }
            catch (ServerException err)
            {
                if (isLicenseError(err))
                {
                    const string DEFAULT_VERSION = "your licence type";
                    string version = getVersionName(err.Message);
                    if (String.IsNullOrWhiteSpace(version))
                    {
                        version = DEFAULT_VERSION;
                    }
                    else
                    {
                        version = "version " + version;
                    }
                    throw new ConnectionException($"Session number exceeds the maximum allowed for {version}.", err);
                }
                else
                {
                    throw new LoginException("Invalid login or password.", err);
                }
            }

            return auth.User;
        }


        /// <summary>
        /// Disconnect from UgCS server.
        /// If connection is not established then <see cref="InvalidOperationException"/> is thrown.
        /// </summary>
        public void Disconnect()
        {
            if (!IsConnected)
                throw new InvalidOperationException("Connection is not established.");

            if (_clientId != null)
            {
                Execute<LogoutResponse>(
                    new LogoutRequest
                    {
                        ClientId = _clientId.Value,
                    });
            }
            try
            {
                _tcpClient.Close();
            }
            catch (Exception e)
            {
                // If no errors then state is released in Colosed event handler
                releaseState();
                throw;
            }
        }

        private void releaseState()
        {
            _clientId = null;
            _user = null;

            if (_messageExecutor != null)
            {
                _messageExecutor.Receiver.RemoveListener(NOTIFICATION_LISTENER_ID);
                _messageExecutor.Close();
                _messageExecutor = null;
            }
        }

        /// <summary>
        /// Cancel subscription for selected vehicle change
        /// </summary>
        /// <param name="subscriptionId"></param>
        public void CancelSubscription(int subscriptionId)
        {
            if (!IsConnected)
                return;
            UnsubscribeEventRequest request = new UnsubscribeEventRequest
            {
                ClientId = ClientId,
                SubscriptionId = subscriptionId,
            };
            Submit<UnsubscribeEventResponse>(request,
                futureResult =>
                {
                    if (futureResult.Exception != null)
                    {
                        _log.Error("Error while canceling subscrription", futureResult.Exception);
                    }
                });
        }


        private bool isVehicleCorrespondsTailNumber(string tailNumber, Vehicle v)
        {
            return v.SerialNumber == tailNumber;
        }


        private void tcpClientSession_onDisconnected(object s, EventArgs e)
        {
            // We don't know if session can be reused or no, so unsubscribe
            ((TcpClientSession)s).Disconnected -= tcpClientSession_onDisconnected;

            releaseState();

            // Connected event should be raised before Disconnected
            lock (_connectSync)
            {
                Disconnected?.Invoke(s, e);
            }
        }

        private bool isLicenseError(Exception ex)
        {
            return ex != null &&
                ex is ServerException &&
                ((ServerException)ex).Error != null &&
                ((ServerException)ex).Error.ErrorCode == LICENSE_CONSTRAINT_ERROR;
        }

        private String getVersionName(String messageText)
        {
            var match = Regex.Match(messageText, VERSION_PATTERN);
            if (match.Success)
            {
                return match.Groups["version"].Value;
            }
            return String.Empty;
        }

        private static Exception newConnectionNotEstablisherException()
        {
            return new InvalidOperationException("Connection not established.");
        }
    }
}
