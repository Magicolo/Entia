using System;
using System.Collections.Generic;
using System.Linq;
using Entia.Core;

namespace Entia.Experiment.V4
{
    public enum From : byte { Top, Bottom }

    public readonly struct Families
    {
        static Entity Self(World.Datum datum) => datum.Chunk.Entities[datum.Index];
        static ref Entity Parent(World.Datum datum) => ref datum.Chunk.Parents[datum.Index];
        static ref (Entity[] items, int count) Children(World.Datum datum) => ref datum.Chunk.Children[datum.Index];

        readonly World _world;

        public Families(World world) => _world = world;

        public Entity Root(Entity entity)
        {
            while (TryParent(entity, out var parent)) entity = parent;
            return entity;
        }

        public IEnumerable<Entity> Roots()
        {
            foreach (var segment in _world.Segments)
                foreach (var chunk in segment.Chunks)
                    for (int i = 0; i < chunk.Count; i++)
                        if (chunk.Parents[i] == Entity.Zero)
                            yield return chunk.Entities[i];
        }

        public bool TryParent(Entity child, out Entity parent) => (parent = Parent(child)) != Entity.Zero;
        public Entity Parent(Entity child) => _world.TryDatum(child, out var datum) ? Parent(datum) : Entity.Zero;
        public ReadOnlySpan<Entity> Children(Entity parent) => _world.TryDatum(parent, out var datum) ? Children(datum).AsSpan() : Array.Empty<Entity>();
        public IEnumerable<Entity> Siblings(Entity child) => _world.TryDatum(Parent(child), out var datum) ? Children(datum).Except(child) : Array.Empty<Entity>();
        public IEnumerable<Entity> Family(Entity entity, From from = From.Top) => Descendants(Root(entity), from, true);

        public IEnumerable<Entity> Ancestors(Entity child, bool self = false)
        {
            if (_world.TryDatum(child, out _))
            {
                if (self) yield return child;
                while (TryParent(child, out child)) yield return child;
            }
        }

        public IEnumerable<Entity> Descendants(Entity parent, From from = From.Top, bool self = false)
        {
            if (_world.TryDatum(parent, out var datum))
            {
                if (self && from == From.Top) yield return parent;
                var (items, count) = Children(datum);
                for (int i = 0; i < count; i++)
                    foreach (var descendant in Descendants(items[i], from, true))
                        yield return descendant;
                if (self && from == From.Bottom) yield return parent;
            }
        }

        public bool Has(Entity parent, Entity child) => _world.TryDatum(child, out var datum) && Has(parent, datum);

        public bool Adopt(Entity parent, Entity child) => _world.TryDatum(parent, out var datum) && Adopt(datum, child);
        public bool Adopt(Entity parent, params Entity[] children)
        {
            var adopted = false;
            if (_world.TryDatum(parent, out var datum))
                for (int i = 0; i < children.Length; i++)
                    adopted |= Adopt(datum, children[i]);
            return adopted;
        }

        public bool AdoptAt(int index, Entity parent, Entity child) => _world.TryDatum(parent, out var datum) && AdoptAt(index, datum, child);
        public bool AdoptAt(int index, Entity parent, params Entity[] children)
        {
            var adopted = false;
            if (_world.TryDatum(parent, out var datum))
                for (int i = 0; i < children.Length; i++)
                    adopted |= AdoptAt(index + i, datum, children[i]);
            return adopted;
        }

        public bool Reject(Entity child) => _world.TryDatum(child, out var datum) && Reject(datum);
        public bool Reject(params Entity[] children)
        {
            var success = false;
            for (int i = 0; i < children.Length; i++) success |= Reject(children[i]);
            return success;
        }

        public bool RejectAt(int index, Entity parent) => _world.TryDatum(parent, out var datum) && RejectAt(index, datum);
        public bool RejectAt(int index, int count, Entity parent)
        {
            var rejected = false;
            if (_world.TryDatum(parent, out var datum))
                for (int i = 0; i < count; i++)
                    rejected |= RejectAt(index, datum);
            return rejected;
        }

        public bool Replace(Entity child, Entity replacement) => _world.TryDatum(replacement, out var datum) && Replace(child, datum);

        bool Has(Entity parent, World.Datum child) => _world.TryDatum(parent, out var datum) && Has(datum, child);
        bool Has(World.Datum parent, World.Datum child) => Parent(child) == Self(parent);

        bool Adopt(World.Datum parent, Entity child) => _world.TryDatum(child, out var datum) && Adopt(parent, datum);
        bool Adopt(World.Datum parent, World.Datum child) => AdoptAt(Children(parent).count, parent, child);
        bool AdoptAt(int index, World.Datum parent, Entity child) => _world.TryDatum(child, out var datum) && AdoptAt(index, parent, datum);
        bool AdoptAt(int index, World.Datum parent, World.Datum child)
        {
            var selves = (parent: Self(parent), child: Self(child));
            if (selves.parent == selves.child) return false;

            // The child must be rejected from its current parent
            Reject(child);
            // All ancestors must be rejected from the child's children to ensure that no family loop is created
            var ancestor = parent;
            do if (Reject(child, ancestor)) break;
            while (_world.TryDatum(Parent(ancestor), out ancestor));

            Children(parent).Insert(index, selves.child);
            Parent(child) = selves.parent;
            return true;
        }

        bool Reject(World.Datum child) => _world.TryDatum(Parent(child), out var datum) && Reject(datum, child);
        bool Reject(World.Datum parent, World.Datum child) =>
            Parent(child) == Self(parent) &&
            Children(parent).IndexOf(Self(child)).TryValue(out var index) &&
            RejectAt(index, parent, child);
        bool RejectAt(int index, World.Datum parent) =>
            Children(parent).TryGet(index, out var child) &&
            _world.TryDatum(child, out var datum) &&
            RejectAt(index, parent, datum);
        bool RejectAt(int index, World.Datum parent, World.Datum child)
        {
            if (Children(parent).RemoveAt(index))
            {
                Parent(child) = default;
                return true;
            }
            return false;
        }

        bool Replace(Entity child, World.Datum replacement) => _world.TryDatum(child, out var datum) && Replace(datum, replacement);
        bool Replace(World.Datum child, World.Datum replacement) => _world.TryDatum(Parent(child), out var datum) && Replace(child, datum, replacement);
        bool Replace(World.Datum child, World.Datum parent, World.Datum replacement)
        {
            var self = Self(child);
            if (self == Self(replacement)) return false;
            return
                Children(parent).IndexOf(self).TryValue(out var index) &&
                RejectAt(index, parent, child) &&
                AdoptAt(index, parent, replacement);
        }
    }

    public static partial class Extensions
    {
        public static Span<T> AsSpan<T>(this (T[] items, int count) source) where T : unmanaged =>
            source.items.AsSpan(0, source.count);
    }
}