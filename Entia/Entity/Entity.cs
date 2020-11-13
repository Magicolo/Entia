﻿using Entia.Core;
using Entia.Core.Documentation;
using Entia.Dependencies;
using Entia.Dependers;
using Entia.Modules;
using Entia.Modules.Query;
using Entia.Queriers;
using Entia.Experimental.Serializers;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Entia
{
    /// <summary>
    /// Represents a world-unique identifier used to logically group components.
    /// </summary>
    /// <seealso cref="Queryables.IQueryable" />
    [ThreadSafe]
    [DebuggerTypeProxy(typeof(View))]
    public readonly struct Entity : IEquatable<Entity>, IComparable<Entity>, Queryables.IQueryable
    {
        sealed class View
        {
            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public object Items
            {
                get
                {
                    var worlds = World.Instances()
                        .Where(world => world.Entities().Has(_entity))
                        .ToArray();
                    return worlds.Length switch
                    {
                        0 => null,
                        1 => new EntityView(_entity, worlds[0]),
                        _ => worlds.Select(world => new EntityView(_entity, world)).ToArray(),
                    };
                }
            }

            readonly Entity _entity;

            public View(Entity entity) { _entity = entity; }
        }

        sealed class EntityView
        {
            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            public Entity Entity { get; }
            public World World { get; }
            public string Name => Entity.Name(World);
            public int Index => Entity.Index;
            public uint Generation => Entity.Generation;
            public long Identifier => Entity.Identifier;
            public ComponentView[] Components => World.TryGet<Modules.Components>(out var components) ?
                components.Get(Entity, States.All).Select(component => new ComponentView(component, Entity, World)).ToArray() :
                Array.Empty<ComponentView>();
            public EntityView Parent => World.TryGet<Modules.Families>(out var families) &&
                families.Parent(Entity) is var parent && parent ?
                new EntityView(parent, World) : null;
            public EntityView[] Children => World.TryGet<Modules.Families>(out var families) ?
                families.Children(Entity).Select(child => new EntityView(child, World)).ToArray() :
                Array.Empty<EntityView>();

            public EntityView(Entity entity, World world)
            {
                Entity = entity;
                World = world;
            }

            public override string ToString() => $"{{ World: {World}, Name: {Name} }}";
        }

        sealed class ComponentView
        {
            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public IComponent Component { get; }
            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            public Entity Entity { get; }
            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            public World World { get; }
            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            public States State => World.TryGet<Modules.Components>(out var components) ?
                components.State(Entity, Component.GetType()) : States.None;

            public ComponentView(IComponent component, Entity entity, World world)
            {
                Component = component;
                Entity = entity;
                World = world;
            }

            public override string ToString() => $"{{ {Component}, {State} }}";
        }

        /// <summary>
        /// A zero initialized entity that will always be invalid.
        /// </summary>
        public static readonly Entity Zero;

        [Implementation]
        static Serializer<Entity> _serializer => Serializer.Blittable.Object<Entity>();
        [Implementation]
        static Querier<Entity> _querier => Querier.From(context => new Query<Entity>(index => context.Segment.Entities.items[index]));
        [Implementation]
        static IDepender _depender => Depender.From(new Read(typeof(Entity)));

        /// <summary>
        /// Implements the operator ==.
        /// </summary>
        public static bool operator ==(Entity left, Entity right) => left.Equals(right);
        /// <summary>
        /// Implements the operator !=.
        /// </summary>
        public static bool operator !=(Entity left, Entity right) => !left.Equals(right);
        /// <summary>
        /// Implements and implicit <c>bool</c> operator.
        /// </summary>
        /// <returns>Returns <c>true</c> if the entity is valid; otherwise, <c>false</c>.</returns>
        public static implicit operator bool(Entity entity) => !entity.Equals(Zero);

        /// <summary>
        /// The world-unique identifier.
        /// </summary>
        public long Identifier => (long)Index | ((long)Generation << 32);

        /// <summary>
        /// The index where the entity is stored within its world.
        /// </summary>
        public readonly int Index;
        /// <summary>
        /// The generation of the index.
        /// </summary>
        public readonly uint Generation;

        public Entity(int index, uint generation)
        {
            Index = index;
            Generation = generation;
        }

        public Entity(long identifier) : this((int)identifier, (uint)(identifier >> 32)) { }

        /// <inheritdoc cref="IComparable{T}.CompareTo(T)"/>
        public int CompareTo(Entity other)
        {
            if (Index < other.Index) return -1;
            else if (Index > other.Index) return 1;
            else if (Generation < other.Generation) return -1;
            else if (Generation > other.Generation) return 1;
            else return 0;
        }
        /// <inheritdoc cref="IEquatable{T}.Equals(T)"/>
        public bool Equals(Entity other) => Index == other.Index && Generation == other.Generation;
        /// <inheritdoc cref="ValueType.Equals(object)"/>
        public override bool Equals(object obj) => obj is Entity entity && Equals(entity);
        /// <inheritdoc cref="ValueType.GetHashCode"/>
        public override int GetHashCode() => Index ^ (int)Generation;
        /// <inheritdoc cref="ValueType.ToString()"/>
        public override string ToString() => $"{{ Index: {Index}, Generation: {Generation} }}";
    }
}
