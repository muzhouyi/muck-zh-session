namespace MuckSaveGame
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    public sealed class PeerProtocolRegistry
    {
        private sealed class Proof { public ulong SteamId; public object Connection; public string Version; }
        private readonly Dictionary<int, Proof> proofs = new Dictionary<int, Proof>();
        public void Confirm(int client, ulong steamId, object connection, string version)
        {
            if (steamId == 0 || connection == null || string.IsNullOrEmpty(version)) return;
            proofs[client] = new Proof { SteamId = steamId, Connection = connection, Version = version };
        }
        public bool Verified(int client, ulong steamId, object connection, string version)
        {
            return proofs.TryGetValue(client, out var proof) && proof.SteamId == steamId &&
                ReferenceEquals(proof.Connection, connection) && proof.Version == version;
        }
        public void Clear() { proofs.Clear(); }
        public static byte[] Hello(string version) { return Encoding.ASCII.GetBytes("MuckSession/1/" + version); }
        public static string ParseHello(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 14 || bytes.Length > 64) return null;
            string value = Encoding.ASCII.GetString(bytes);
            const string prefix = "MuckSession/1/";
            if (!value.StartsWith(prefix, StringComparison.Ordinal)) return null;
            string version = value.Substring(prefix.Length);
            foreach (char c in version) if (!(c >= '0' && c <= '9') && c != '.') return null;
            return version.Length == 0 ? null : version;
        }
    }
}
