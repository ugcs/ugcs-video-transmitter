using UGCS.Sdk.Protocol.Encoding;

namespace UcsService
{
    public class LicenseService
    {
        private ConnectionService _connectionService;

        public LicenseService(ConnectionService cs)
        {
            _connectionService = cs;
        }

        public bool HasVideoPlayerPermission()
        {
            GetLicenseRequest request = new GetLicenseRequest
            {
                ClientId = _connectionService.ClientId
            };
            
            GetLicenseResponse response = _connectionService.Execute<GetLicenseResponse>(request);
            return response.LicensePermissions.UgcsVideoPlayer;
        }
    }
}
