namespace MuckSaveGame
{
    using System;
    // Independent of Unity and Steam so the join/rejoin timing can be regression-tested.
    public sealed class LobbySyncState
    {
        private ulong lobby;
        private float nextPublish;
        private string lastNotice;
        private float nextNotice;
        public void PublishIfDue(ulong currentLobby, float now, Action publish)
        {
            if (currentLobby == 0) { lobby = 0; nextPublish = 0; return; }
            if (currentLobby != lobby) { lobby = currentLobby; nextPublish = now; }
            if (now < nextPublish) return;
            nextPublish = now + 2f;
            publish();
        }
        public bool ShouldNotify(string message, float now)
        {
            if (message == lastNotice && now < nextNotice) return false;
            lastNotice = message; nextNotice = now + 30f;
            return true;
        }
        public void ClearNotice() { lastNotice = null; nextNotice = 0; }
        public void Reset() { lobby = 0; nextPublish = 0; ClearNotice(); }
    }
}
