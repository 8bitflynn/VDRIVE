namespace VDRIVE_Contracts.Interfaces
{
    public interface IProtocolHandler
    {
        void HandleClient(ISessionProvider sessionManager);
    }
}
