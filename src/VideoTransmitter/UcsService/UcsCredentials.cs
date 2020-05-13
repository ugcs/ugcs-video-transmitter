
namespace UcsService
{
    public sealed class UcsCredentials
    {
        public string Login { get; private set; }
        public string Password { get; private set; }


        public UcsCredentials(string login, string password)
        {
            Login = login;
            Password = password;
        }
    }
}
