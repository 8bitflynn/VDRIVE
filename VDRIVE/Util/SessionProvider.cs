using System.Collections.Concurrent;
using VDRIVE.Drive;
using VDRIVE.Floppy;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE.Util
{
    public class SessionProvider : ISessionProvider
    {
        public SessionProvider(IConfiguration configuration)
        {
            this.Configuration = configuration;
        }
        protected IConfiguration Configuration;        

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

                ILogger sessionLogger = new SessionLogger(session.SessionId);
                session.ProcessRunner = new LockingProcessRunner(this.Configuration, sessionLogger);
                session.FloppyResolver = FloppyResolverFactory.CreateFloppyResolver(this.Configuration.FloppyResolver, this.Configuration, sessionLogger, session.ProcessRunner);
                session.StorageAdapter = StorageAdapterFactory.CreateStorageAdapter(this.Configuration.StorageAdapter, session.ProcessRunner, this.Configuration, sessionLogger);

                VDriveClients.GetOrAdd(session.SessionId, session);
                sessionLogger.LogMessage($"*** Created new session with SessionId: {session.SessionId}", VDRIVE_Contracts.Enums.LogSeverity.Info);
            }
            else
            {             
                session = VDriveClients[sessionId];
                session.ClientInfo.LastAccess = DateTime.Now;

                ILogger sessionLogger = new SessionLogger(session.SessionId);
                sessionLogger.LogMessage($"*** Existing session with SessionId: {session.SessionId}", VDRIVE_Contracts.Enums.LogSeverity.Info);
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
                            ILogger sessionLogger = new SessionLogger(removedSession.SessionId);
                            sessionLogger.LogMessage($"*** Expired session with SessionId: {session.Key}", VDRIVE_Contracts.Enums.LogSeverity.Info);
                        }
                    }
                }
            });
        }
    }
}
