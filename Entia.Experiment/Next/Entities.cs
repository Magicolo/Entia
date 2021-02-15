using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.RegularExpressions;
using Entia.Core;

namespace Entia.Experiment.V4
{
    public readonly struct Entities : IEnumerable<Entities.Enumerator, Entity>
    {
        public struct Enumerator : IEnumerator<Entity>
        {
            public Entity Current => _chunk.Entities[_indices.entity];
            object IEnumerator.Current => Current;

            readonly World _world;
            (int segment, int chunk, int entity) _indices;
            Segment _segment;
            Segment.Chunk _chunk;

            public Enumerator(World world)
            {
                _world = world;
                _indices = (-1, -1, -1);
                _segment = default;
                _chunk = default;
            }

            public bool MoveNext()
            {
                while (true)
                {
                    if (_segment == null && !_world.Segments.TryAt(++_indices.segment, out _segment)) return false;
                    else if (_chunk == null && !_segment.Chunks.TryAt(++_indices.chunk, out _chunk))
                    {
                        _indices.chunk = -1;
                        _segment = null;
                    }
                    else if (++_indices.entity < _chunk.Count) return true;
                    else
                    {
                        _indices.entity = -1;
                        _chunk = null;
                    }
                }
            }

            public void Reset() { _indices = (-1, -1, -1); _segment = default; _chunk = default; }
            public void Dispose() => this = default;
        }

        public struct AncestorEnumerator : IEnumerator<Entity>
        {
            public Entity Current => _current;
            object IEnumerator.Current => Current;
            readonly World _world;
            readonly Entity _entity;
            Entity _next;
            Entity _current;

            public AncestorEnumerator(Entity entity, World world)
            {
                _world = world;
                _entity = entity;
                _next = entity;
                _current = entity;
            }

            public bool MoveNext()
            {
                if (_world.TryDatum(_next, out var datum))
                {
                    _current = _next;
                    _next = datum.Parent;
                    return true;
                }
                return false;
            }

            public void Reset() => _next = _entity;
            public void Dispose() => this = default;
        }

        public readonly struct AncestorEnumerable : IEnumerable<AncestorEnumerator, Entity>
        {
            readonly Entity _entity;
            readonly World _world;
            public AncestorEnumerable(Entity entity, World world) { _entity = entity; _world = world; }
            public AncestorEnumerator GetEnumerator() => new(_entity, _world);
            IEnumerator<Entity> IEnumerable<Entity>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        readonly World _world;
        public Entities(World world) { _world = world; }

        public bool Has(Entity entity) => _world.TryDatum(entity, out _);
        public AncestorEnumerable Ancestors(Entity entity, bool self) =>
            self ? new(entity, _world) :
            _world.TryDatum(entity, out var datum) ? new(datum.Parent, _world) :
            new(Entity.Zero, _world);

        public Enumerator GetEnumerator() => new(_world);
        IEnumerator<Entity> IEnumerable<Entity>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}