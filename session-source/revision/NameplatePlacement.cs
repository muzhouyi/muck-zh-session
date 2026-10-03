namespace MuckSaveGame
{
    using System;
    public struct NameplatePoint { public float X, Y; public bool Edge; public string Arrow; }
    public static class NameplatePlacement
    {
        public static NameplatePoint Place(float x, float y, float depth, float width, float height, float marginX, float marginY)
        {
            float cx = width * 0.5f, cy = height * 0.5f;
            float hx = Math.Max(1f, cx - Math.Min(marginX, width * 0.4f));
            float hy = Math.Max(1f, cy - Math.Min(marginY, height * 0.4f));
            float dx = x - cx, dy = y - cy;
            bool edge = depth <= 0 || Math.Abs(dx) > hx || Math.Abs(dy) > hy;
            if (!edge) return new NameplatePoint { X = x, Y = y, Edge = false, Arrow = "" };
            if (depth <= 0) { dx = -dx; dy = -dy; }
            if (Math.Abs(dx) + Math.Abs(dy) < 0.001f) dy = -1;
            float scale = Math.Min(Math.Abs(dx) < 0.001f ? float.MaxValue : hx / Math.Abs(dx),
                                   Math.Abs(dy) < 0.001f ? float.MaxValue : hy / Math.Abs(dy));
            return new NameplatePoint { X = Math.Max(cx-hx, Math.Min(cx+hx, cx + dx * scale)), Y = Math.Max(cy-hy, Math.Min(cy+hy, cy + dy * scale)), Edge = true,
                Arrow = Math.Abs(dx) > Math.Abs(dy) ? (dx > 0 ? "→" : "←") : (dy > 0 ? "↑" : "↓") };
        }
    }
}
