using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Numerics;
using Entia.Core;

namespace Entia.Experiment.V4
{
    /*
        In V4, we remove the ability for entities to change their structure dynamically post-creation.
        - This means that the complete structure of the entity must be determined at creation time through a template.
        - This means that the segment of an entity can be known at creation time and be valid for its whole life.
        - This solves concurrency issues of moving entities between segments.
        - 'Add<T>/Remove<T>' no longer exist.
        - 'Set<T>' can be removed since components should be modified through a 'ref T Get<T>'.
        - This also ensures that the API is more coherent; 'Has<T>' will always agree with 'Get<T>' and 'Group<T>'.
        - This allows to validate the requirements of an entity once and be guarantee that those requirements hold
        as long as the entity is alive which reduces the amount of checks required and allows to declare
        requirements statically. See 'Entity<T>'.
        - The case for memory consumption is harder to determine. On one hand, entities will require more memory
        since they will need to hold additional state to enable/disable some behavior which could've been accomplished
        through the addition/removal of components. On the other hand, static entities will require much less segments.
        The balance likely favors static entities, but this remains to be shown.
        - Segments iteration will be lock free ('Destroy' might be an issue).
        - This reduces the power of queries, but may be a tolerable trade-off.
        - Since segments will be known at system schedule time, dependencies can be expressed as 'Read/Write' with
        a set of segments. This means that exclusive queries can be run in parallel even if they write to the same
        component type. In principle, new segments could be created from outside the execution loop, which would
        require a rescheduling of systems.

        There could be a few variants for the 'Defer' module:
        - 'Defer.Next': defers operations until the next synchronization point.
        - 'Defer.Previous': defers operations until the previous synchronization point of the next frame.
        - 'Defer.End': defers operations until the end of the frame.

        Since it can not be safely predetermined what segments a 'Destroy' operation will affect, it can be treated
        in 2 ways:
        - Give access the instant 'Destroy' operation which will force a synchronization point.
        - Give access to a deferred 'Destroy' operation through variants of the 'Defer' module.

        Resources should be implemented as an entity:
        - Each resource would be associated with an entity that would be stored in a segment of size 1.
        - There must be 1 entity per resource type since dependencies are expressed in terms of segments
        and to prevent improper detection of dependency conflicts, they must be separated.
        - Resource entities should be excluded from queries.

        Messages should be implemented as temporary entities:
        - 'Emitter<T>' will create a new entity with a single 'T' message component on emit which will be destroyed
        before the next execution of the emitting system.
        - 'Receiver<T>' will query entities with the 'T' message component and enqueue the component in a queue.
        - 'Emitter<T>' will have a 'Write' dependency on the message segment.
        - 'Receiver<T>' will have a 'Read' dependency on the message segment.
        - Therefore, messages can not be emitted and received concurrently which is desired since even if it was
        thread-safe, the order would not be deterministic; a property that Entia tries to preserve.
        - Message entities should be excluded from queries.
        - The implementation will use a 'Template<T>' to emit the message. This will force the
        initialization of the segment before execution.
            - It will destroy the entities using a query of 'T' and 'Defer.Previous'. Since the
            creation/destruction segment is known, the dependency is just a 'Write<T>' rather than
            an 'Unknown'.
            - It will use a query of 'T' that does not exclude messages to retrieve them. This only
            imposes a 'Read<T>' dependency that will include only 1 segment.
        - For systems that consume all messages on each execution, the queue in not needed, the
        messages can read directly from the message segment store.

        Static analysis of systems:
        - It should be possible to produce a warning whenever a component from a given segment is never read/written.
            - This could mean that a template has useless data associated with it.

        System state:
        - It would be pretty useful to allow systems to hold state.
        - The problem is the mapping between a system and its state in such a way that the mapping can be restored.
        - System state would allow the implementation of 'Receiver<T>/Emitter<T>'.
        - System state would allow nodes that execute once every 'n' frames.
        - The mapping will require some kind of system identifier.
            - Maybe a name path? Ex: "root
    */

    /*
    TODO:
    - Add the 'On<T>' node which executes a wrapped node conditionally to the reception of a message of type 'T'.

    FIX:
    - If a system has internally conflicting dependencies, there may be race condictions.
        - Ex: A system may create an entity in a segment that is being iterated on.
        - When conflicts are detected, the 'Runs' array of the sub runner can be combined in one
        synchronous run.

    - Create/Destroy operations can unpredictable (but thread-safe) effects when they affect a segment that is being
    iterated on.
        - Create could visit or not the newly created entity.
        - Destroy could cause an entity to not be visited since it has been moved.

    - The 'If' node must not evaluate the condition for each 'run', but rather once per 'Runner'.
    */

    public static class Test
    {
        public struct OnInitialize { }
        public struct OnFinalize { }

        public struct Game { public bool Quit; }
        public struct Position { public Vector2 Value; }
        public struct Velocity { public Vector2 Value; }
        public struct Scale { public Vector2 Value; }
        public struct Rotation { public double Angle; }
        public struct Motion { public double MoveSpeed, RotateSpeed; }
        public struct ForwardMotion { public double Speed; }
        public struct Lifetime { public double Duration; }
        public struct Sprite { public string Path; public Color Color; }
        public struct Collider { public double Radius; }
        public struct Controller { }
        public struct Weapon { public DamageKinds Kind; public double Amount; }
        public struct Health { public double Current, Maximum; public DamageKinds Damageable; }
        public struct Time { public double Total, Delta; }
        public struct Debug { public string Name; }
        public struct IsObservable { }
        public enum DamageKinds { None = 0, Body = 1 << 0, Bullet = 1 << 1 }

        public static readonly Template<(Vector2 position, double angle)> Player = Create(nameof(Player))
            .Add(Physical(default, 0.25))
            .Add(new Sprite { Path = "Shapes/Triangle", Color = Color.Cyan })
            .Add(new Health { Current = 1.0, Maximum = 1.0, Damageable = DamageKinds.Body })
            .Add(new Motion { MoveSpeed = 0.0, RotateSpeed = 2.0 })
            .Add<Controller>();

        public static readonly Template<(Vector2 position, double angle, double lifetime, double speed, double health)> Enemy = Create(nameof(Enemy))
            .Add(Physical(new(3f, 3f), 1.5))
            .Adapt<(Vector2 position, double angle, double lifetime, double speed, double health)>(state => (state.position, state.angle))
            .Add(state => new Sprite { Path = "Shapes/Square", Color = Color.FromArgb(255, 255, (int)(255 - state.health * 75), 0) })
            .Add(state => new Health { Current = state.health, Maximum = state.health, Damageable = DamageKinds.Bullet })
            .Add(state => new Lifetime { Duration = state.lifetime })
            .Add(state => new ForwardMotion { Speed = state.speed })
            .Add(new Weapon { Kind = DamageKinds.Body, Amount = 1.0 })
            .Add<IsObservable>();

        public static readonly Template<(Vector2 position, double angle, double speed)> Bullet = Create(nameof(Bullet))
            .Add(Physical(new(0.5f, 0.5f), 0.1))
            .Adapt<(Vector2 position, double angle, double speed)>(state => (state.position, state.angle))
            .Add(new Sprite { Path = "Shapes/Circle", Color = Color.Yellow })
            .Add(new Health { Current = 1.0, Maximum = 1.0, Damageable = DamageKinds.Body })
            .Add(new Lifetime { Duration = 3.0 })
            .Add(state => new ForwardMotion { Speed = state.speed })
            .Add(new Weapon { Kind = DamageKinds.Bullet, Amount = 1.0 });

        static Template<Unit> Create(string name) => Template.Empty().Add(new Debug { Name = name });

        static Template<(Vector2 position, double angle)> Physical(Vector2? scale = default, double? radius = default)
        {
            var template = Template.Empty<(Vector2 position, double angle)>()
              .Add(state => new Position { Value = state.position })
              .Add(state => new Rotation { Angle = state.angle })
              .Add<Velocity>();
            if (scale.HasValue) template = template.Add(new Scale { Value = scale.Value });
            if (radius.HasValue) template = template.Add(new Collider { Radius = radius.Value });
            return template;
        }

        [ThreadStatic] static Random _random;
        static readonly Type[] _types = typeof(Test).GetNestedTypes().Where(type => type.IsValueType).ToArray();
        public static Template<Unit> Random(int seed)
        {
            var random = new Random(seed);
            var template = Template.Empty();
            while (random.NextDouble() < 0.9) template = template.Add(_types[random.Next(_types.Length)]);
            return template;
        }

        public static void Do()
        {
            var world = new World();
            var game = world.Resource<Game>();
            var entities = (new Entity[1], new Entity[10], new Entity[100], new Entity[1000]);
            var create = Node.All(
                Enumerable.Range(0, 10).Select(index => Node.Create(Random(64321 + index), creator => creator.Create())).All(),
                Enumerable.Range(0, 10).Select(index => Node.Create(Random(-3572 + index), creator => creator.Create(entities.Item1))).All(),
                Enumerable.Range(0, 10).Select(index => Node.Create(Random(987965432 + index), creator => creator.Create(entities.Item2))).All(),
                Enumerable.Range(0, 10).Select(index => Node.Create(Random(-98764 + index), creator => creator.Create(entities.Item3))).All(),
                Enumerable.Range(0, 10).Select(index => Node.Create(Random(789312 + index), creator => creator.Create(entities.Item4))).All()
            ).Schedule(world);
            var destroy = Node.Destroy().If(() => (_random ??= new()).NextDouble() < 0.1).Schedule(world);
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 10_000; i++)
            {
                create();
                destroy();
                if ((i % 100) == 0)
                {
                    Console.WriteLine($"Iteration({i}): {watch.Elapsed} | {world.Count}/{world.Capacity}");
                    watch.Restart();
                }
            }
        }
    }

    public struct Or<T1, T2> { }
    public struct And<T1, T2> { }
    public struct Not<T> { }
    public struct Has<T> { }

    public struct Target
    {
        // Express requirements explicitly.
        public Entity<(Targetable, Not<IsInvincible>)> Entity;
    }

    public readonly struct State<T>
    {
        static readonly Template<T> _template = Template.Empty<T>().Add(state => state);

        public ref T Value => ref _store[_index];
        readonly T[] _store;
        readonly int _index;

        public State(T value, World world, byte size = 1)
        {
            var creator = world.Creator(_template, size);
            var entity = creator.Create(value);
            world.TryDatum(entity, out var datum);
            _index = datum.Index;
            _store = (T[])datum.Chunk.Stores[0];
        }
    }

    public static class Defer
    {
        // Defers to the next synchronization point.
        public readonly struct Next
        {
            public bool Destroy(Entity entity) => throw null;
        }

        // Defers to the previous synchronization point (of the next frame).
        public readonly struct Previous
        {
            public bool Destroy(Entity entity) => throw null;
        }

        // Defers to the end of the frame.
        public readonly struct End
        {
            public bool Destroy(Entity entity) => throw null;
        }

        // Defers to the next emission of a message of type 'T'.
        public readonly struct On<T>
        {
            public bool Destroy(Entity entity) => throw null;
        }
    }
}