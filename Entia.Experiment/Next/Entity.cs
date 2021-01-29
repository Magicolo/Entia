using System;
using System.Runtime.InteropServices;

namespace Entia.Experiment.V4
{
    [StructLayout(LayoutKind.Explicit)]
    public readonly struct Entity : IEquatable<Entity>, IComparable<Entity>, Queryables.IQueryable
    {
        public static readonly Entity Zero;

        public static bool operator ==(Entity left, Entity right) => left.Identifier == right.Identifier;
        public static bool operator !=(Entity left, Entity right) => left.Identifier != right.Identifier;

        [FieldOffset(0)] public readonly long Identifier;
        [FieldOffset(0)] public readonly int Index;
        [FieldOffset(4)] public readonly uint Generation;

        public Entity(int index, uint generation)
        {
            Identifier = default;
            Index = index;
            Generation = generation;
        }

        public int CompareTo(Entity other) => Identifier.CompareTo(other.Identifier);
        public bool Equals(Entity other) => this == other;
        public override bool Equals(object obj) => obj is Entity entity && this == entity;
        public override int GetHashCode() => Identifier.GetHashCode();
        public override string ToString() => $"{{ Index: {Index}, Generation: {Generation} }}";
    }

    // The only way to obtain an 'Entity<T>' where 'T' represents some requirements is through the 'Components'
    // module which has to validate that an 'Entity' does satisfy 'T'. If entities cannot change their structure
    // post-creation, the 'Entity' will always satisfy 'T' as long as it lives so the only check that remains
    // is the check for life since the checks for component existence are unnecessary.
    public readonly struct Entity<T>
    {
        public static implicit operator Entity(Entity<T> entity) => entity._entity;

        readonly Entity _entity;
        public Entity(Entity entity) { _entity = entity; }
    }
}