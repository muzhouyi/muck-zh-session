namespace MuckSaveGame
{
    using System;
    using System.Collections.Generic;
    // Pure item arithmetic. Every accepted transfer conserves quantities by item id.
    public struct ItemStack : IEquatable<ItemStack>
    {
        public int Id, Amount;
        public ItemStack(int id, int amount) { Id = id >= 0 && amount > 0 ? id : -1; Amount = id >= 0 && amount > 0 ? amount : 0; }
        public bool Empty { get { return Id < 0 || Amount <= 0; } }
        public bool Equals(ItemStack other) { return Id == other.Id && Amount == other.Amount; }
    }
    public struct TradeResult { public ItemStack Slot, Mouse; }
    public sealed class ReceiptBook<T>
    {
        private readonly Dictionary<string, T> records = new Dictionary<string, T>();
        public T RunOnce(string key, Func<T> operation)
        {
            T result; if (records.TryGetValue(key, out result)) return result;
            result = operation(); records.Add(key, result); return result;
        }
        public void Clear() { records.Clear(); }
    }
    public static class ContainerTrade
    {
        public static TradeResult Click(ItemStack slot, ItemStack mouse, bool right, bool stackable, int max)
        {
            var result = new TradeResult { Slot = slot, Mouse = mouse };
            if (mouse.Empty)
            {
                if (slot.Empty) return result;
                int taken = right ? (slot.Amount + 1) / 2 : slot.Amount;
                result.Mouse = new ItemStack(slot.Id, taken);
                result.Slot = new ItemStack(slot.Id, slot.Amount - taken);
            }
            else if (slot.Empty || (slot.Id == mouse.Id && stackable))
            {
                int existing = slot.Empty ? 0 : slot.Amount;
                int count = Math.Min(right ? 1 : mouse.Amount, Math.Max(0, max - existing));
                result.Slot = new ItemStack(mouse.Id, existing + count);
                result.Mouse = new ItemStack(mouse.Id, mouse.Amount - count);
            }
            else { result.Slot = mouse; result.Mouse = slot; }
            return result;
        }
    }
}
