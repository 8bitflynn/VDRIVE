using System.Collections.Concurrent;
using VDRIVE.Drive;
using VDRIVE.Floppy;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE.Util
{
    public class SessionProvider : ISessionProvider
    {
        public SessionProvider(IConfiguration configuration, ILogger logger)
        {
            this.Configuration = configuration;
            this.Logger = logger;
        }
        protected IConfiguration Configuration;
        protected ILogger Logger;

        // session / client info for stateless HTTP
        private static readonly ConcurrentDictionary<ushort, Session> VDriveClients = new ConcurrentDictionary<ushort, Session>();

        public Session GetOrCreateSession(ushort sessionId)
        {
            this.RemoveExpiredSessions();

            Session session = null;

            if (sessionId == 0 || !VDriveClients.ContainsKey(sessionId))
            {
                // instance dependencies per client for concurrency
                // and store in local session
                session = new Session();
                session.ClientInfo.ConnectedAt = DateTime.Now;
                session.ClientInfo.LastAccess = DateTime.Now;                
                if (!VDriveClients.Any())
                {
                    session.SessionId = 1;
                }
                else
                {
                    session.SessionId = (ushort)(VDriveClients.Max(m => m.Key) + 1);
                }

                session.ProcessRunner = new LockingProcessRunner(this.Configuration, this.Logger);
                session.FloppyResolver = FloppyResolverFactory.CreateFloppyResolver(this.Configuration.FloppyResolver, this.Configuration, this.Logger, session.ProcessRunner);
                session.StorageAdapter = StorageAdapterFactory.CreateStorageAdapter(this.Configuration.StorageAdapter, session.ProcessRunner, this.Configuration, this.Logger);

                VDriveClients.GetOrAdd(session.SessionId, session);
                this.Logger.LogMessage($"Created new session with SessionId: {session.SessionId}", VDRIVE_Contracts.Enums.LogSeverity.Info);
            }
            else
            {             
                session = VDriveClients[sessionId];
                session.ClientInfo.LastAccess = DateTime.Now;
                
                this.Logger.LogMessage($"Existing session with SessionId: {session.SessionId}", VDRIVE_Contracts.Enums.LogSeverity.Info);
            }           

            return session;
        }

        private void RemoveExpiredSessions()
        {
            // cleanup expired sessions in background
            Task.Run(() =>
            {
                foreach (KeyValuePair<ushort, Session> session in VDriveClients)
                {
                    DateTime currentDateTime = DateTime.Now;
                    TimeSpan timeSpanSinceLastAccess = currentDateTime - session.Value.ClientInfo.LastAccess.Value;
                    if (timeSpanSinceLastAccess.TotalMinutes >= this.Configuration.SessionTimeoutMinutes.Value)
                    {
                        Session removedSession;
                        if (VDriveClients.TryRemove(session.Key, out removedSession))
                        {
                            this.Logger.LogMessage($"*** Expired session with SessionId: {session.Key}", VDRIVE_Contracts.Enums.LogSeverity.Info);
                        }
                    }
                }
            });
        }
    }
}
