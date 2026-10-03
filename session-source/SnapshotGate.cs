namespace MuckSaveGame
{
    using System.Collections.Generic;
    public sealed class SnapshotGate
    {
        private readonly Dictionary<int, string> expected = new Dictionary<int, string>();
        private readonly Dictionary<int, int> received = new Dictionary<int, int>();
        public int Generation { get; private set; }
        public void Begin(Dictionary<int, string> peers)
        {
            expected.Clear(); received.Clear();
            Generation = unchecked(Generation + 1);
            foreach (var peer in peers) { expected.Add(peer.Key, peer.Value); received.Add(peer.Key, 0); }
        }
        public bool Accept(int client, string steamId, int generation)
        { return generation == Generation && expected.TryGetValue(client, out var id) && id == steamId; }
        public void Mark(int client, int part)
        { if (received.ContainsKey(client)) received[client] |= part; }
        public bool Complete
        {
            get { foreach (int mask in received.Values) if (mask != 31) return false; return true; }
        }
        public void Clear() { expected.Clear(); received.Clear(); }
    }
}
