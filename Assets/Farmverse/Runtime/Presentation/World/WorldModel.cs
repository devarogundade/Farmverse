#nullable enable
using System;

namespace Farmverse.Presentation.World
{
    /// <summary>A point on the ground plane (x right, z forward), in world units (metres).</summary>
    public readonly struct GroundPoint : IEquatable<GroundPoint>
    {
        public GroundPoint(float x, float z)
        {
            X = x;
            Z = z;
        }

        public float X { get; }
        public float Z { get; }

        public static GroundPoint operator +(GroundPoint a, GroundPoint b) => new GroundPoint(a.X + b.X, a.Z + b.Z);

        public bool Equals(GroundPoint other) => Math.Abs(X - other.X) < 1e-4f && Math.Abs(Z - other.Z) < 1e-4f;
        public override bool Equals(object? obj) => obj is GroundPoint p && Equals(p);
        public override int GetHashCode() => HashCode.Combine(Math.Round(X, 3), Math.Round(Z, 3));
        public override string ToString() => $"({X:0.###}, {Z:0.###})";
    }

    /// <summary>The visual stage of a thing: picks the prefab variant.</summary>
    public enum VisualStage
    {
        None = 0,
        Seeded = 1,
        Sprout = 2,
        Young = 3,
        Grown = 4,
        Ready = 5,
        Working = 6,
        Idle = 7,
    }

    /// <summary>Badges over a thing: the world shows what needs doing.</summary>
    [Flags]
    public enum VisualFlags
    {
        None = 0,
        /// <summary>A pen waiting to be fed.</summary>
        Hungry = 1 << 1,
        /// <summary>A ripe field or a tree with fruit.</summary>
        Harvestable = 1 << 4,
        /// <summary>A pen's produce or a machine's goods are ready.</summary>
        Collectable = 1 << 5,
        /// <summary>A prediction is waiting for the server.</summary>
        Pending = 1 << 6,
    }

    /// <summary>
    /// One thing the world draws. <see cref="Key"/> is stable across updates so the
    /// view reuses its object; <see cref="Target"/> is what a tap on it selects
    /// (a thing id; scenery taps pass through).
    /// </summary>
    public sealed class WorldVisual : IEquatable<WorldVisual>
    {
        public WorldVisual(string key, string target, string visualId, VisualStage stage, VisualFlags flags, GroundPoint position, int progressPercent,
            int rotation = 0, string? label = null, bool pickable = true, string? text = null)
        {
            Key = key;
            Target = target;
            VisualId = visualId;
            Stage = stage;
            Flags = flags;
            Position = position;
            ProgressPercent = progressPercent;
            Rotation = rotation;
            Label = label;
            Pickable = pickable;
            Text = text;
        }

        public string Key { get; }
        /// <summary>What this visual belongs to: a thing id (<c>p12</c>), or a cell key for paths and fences.</summary>
        public string Target { get; }
        /// <summary>A catalogue id (crop_wheat, livestock_cow, machine_mill) or a ground id the visual catalogue maps to prefabs.</summary>
        public string VisualId { get; }
        public VisualStage Stage { get; }
        public VisualFlags Flags { get; }
        public GroundPoint Position { get; }
        /// <summary>Whole percent so a growing crop changes the model at most 100 times, not every frame.</summary>
        public int ProgressPercent { get; }
        /// <summary>Facing in quarter turns (0–3).</summary>
        public int Rotation { get; }
        /// <summary>A short sign over the object.</summary>
        public string? Label { get; }
        /// <summary>False for scenery that taps should pass through (paths, fences, soil, animals inside a pen).</summary>
        public bool Pickable { get; }
        /// <summary>Words painted on the object (a signpost's lines, joined by a new line).</summary>
        public string? Text { get; }

        public bool Equals(WorldVisual? other) =>
            other != null && Key == other.Key && Target == other.Target && VisualId == other.VisualId && Stage == other.Stage && Flags == other.Flags
            && Position.Equals(other.Position) && ProgressPercent == other.ProgressPercent && Rotation == other.Rotation && Label == other.Label
            && Pickable == other.Pickable && Text == other.Text;

        public override bool Equals(object? obj) => Equals(obj as WorldVisual);
        public override int GetHashCode() => HashCode.Combine(Key, VisualId, Stage, Flags, ProgressPercent);
    }
}
