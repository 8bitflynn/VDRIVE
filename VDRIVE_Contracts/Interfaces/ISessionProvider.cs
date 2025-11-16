using VDRIVE_Contracts.Structures;

namespace VDRIVE_Contracts.Interfaces
{
    public interface ISessionProvider
    {
        Session GetOrCreateSession(ushort sessionId); 
    }
}
