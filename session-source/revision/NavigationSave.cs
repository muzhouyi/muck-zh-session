namespace MuckSaveGame
{
    using System;
    using System.Collections.Generic;
    using System.Xml.Linq;
    using UnityEngine;
    public sealed class NavigationSave : ISaveDataManager, ISaveData
    {
        public static readonly NavigationSave Instance = new NavigationSave();
        public string Name { get { return "MuckSession.Navigation"; } }
        public string RoomId = "";
        public readonly Dictionary<string, Vector3> Homes = new Dictionary<string, Vector3>();
        public const int MaxHomes = 8;
        public readonly Dictionary<string, SortedDictionary<int, Vector3>> Places = new Dictionary<string, SortedDictionary<int, Vector3>>();
        public SortedDictionary<int, Vector3> GetHomes(string owner)
        {
            if (!Places.TryGetValue(owner, out var places)) { places = new SortedDictionary<int, Vector3>(); Places[owner] = places; }
            if (Homes.TryGetValue(owner, out var legacy) && !places.ContainsKey(1)) places[1] = legacy;
            return places;
        }
        public void SetHome(string owner, int slot, Vector3 position)
        {
            if (slot < 1 || slot > MaxHomes || !Finite(position.x) || !Finite(position.y) || !Finite(position.z)) return;
            GetHomes(owner)[slot] = position; if (slot == 1) Homes[owner] = position;
        }
        public void RemoveHome(string owner, int slot)
        { GetHomes(owner).Remove(slot); if (slot == 1) Homes.Remove(owner); }
        public void EnsureWorld() { if (RoomId.Length == 0) RoomId = Guid.NewGuid().ToString("N"); }
        public ISaveData GetSaveData() { EnsureWorld(); return this; }
        public void SaveXml(XElement xml)
        {
            xml.SetAttributeValue("world", RoomId);
            foreach (string owner in new HashSet<string>(System.Linq.Enumerable.Concat(Homes.Keys, Places.Keys)))
                foreach (var pair in GetHomes(owner))
                    xml.Add(new XElement("Home", new XAttribute("player", owner), new XAttribute("slot", pair.Key), new XAttribute("x", pair.Value.x), new XAttribute("y", pair.Value.y), new XAttribute("z", pair.Value.z)));
        }
        public void LoadXml(XElement xml)
        {
            Homes.Clear(); Places.Clear(); RoomId = "";
            Guid world;
            if (Guid.TryParse((string)xml.Attribute("world"), out world)) RoomId = world.ToString("N");
            foreach (var h in xml.Elements("Home"))
            {
                string owner = (string)h.Attribute("player");
                float x, y, z;
                if (!string.IsNullOrEmpty(owner) && float.TryParse((string)h.Attribute("x"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x) &&
                    float.TryParse((string)h.Attribute("y"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y) &&
                    float.TryParse((string)h.Attribute("z"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out z) && Finite(x) && Finite(y) && Finite(z))
                { int slot; if (!int.TryParse((string)h.Attribute("slot"), out slot)) slot = 1; SetHome(owner, slot, new Vector3(x, y, z)); }
            }
        }
        public static bool Finite(float f) { return !float.IsNaN(f) && !float.IsInfinity(f) && Math.Abs(f) < 1000000f; }
        public void ApplyLoadedData() { EnsureWorld(); }
        public void Unload() { RoomId = ""; Homes.Clear(); Places.Clear(); }
    }
}
