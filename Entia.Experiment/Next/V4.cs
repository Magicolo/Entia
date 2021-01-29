using System;
using System.Collections.Concurrent;
using System.Diagnostics;
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
    */

    /*
    FIX
    - If a system has internally conflicting dependencies, there may be race condictions.
        - Ex: A system may create an entity in a segment that is being iterated on.
        - When conflicts are detected, the 'Runs' array of the sub runner can be combined in one
        synchronous run.

    - When running test and breaking after 'populate', entities don't seem to be created properly, or is
    it a bug with the enumerator?
    */

    public static class Test
    {
        public struct OnInitialize { }
        public struct OnFinalize { }

        public struct Position { public Vector3 Value; }
        public struct Velocity { public Vector3 Value; }
        public struct Time { public float Total, Delta; }

        public static void Do()
        {
            var template = Template.Create<Vector3>()
                .Add(state => new Position { Value = state })
                .Add(new Velocity { Value = new(1f, 2f, 3f) });

            var world = new World();
            var populate = Node.All(
                Node.Create(Template.Create().Add(new OnInitialize()), creator => Node.Run(() => creator.Create(default))),
                Node.Create(Template.Create().Add(new OnFinalize()), creator => Node.Run(() => creator.Create(default))),
                Node.Create(Template.Create().Add(new Position()), creator => Node.Run(() => creator.Create(default))),
                Node.Create(Template.Create().Add(new Velocity()), creator => Node.Run(() => creator.Create(default))),
                Node.Create(Template.Create().Add(new Time()), creator => Node.Run(() => creator.Create(default))),
                Node.Create(template.Add(new Time()), creator => Node.Run(() => creator.Create(default))),
                Node.Create(template.Add(new OnInitialize()), creator => Node.Run(() => creator.Create(default))),
                Node.Create(template.Add(new OnFinalize()), creator => Node.Run(() => creator.Create(default))),
                Node.Create(template, creator => Node.Run(() => creator.Create(new(1f, 2f, 3f))))
            ).Schedule(world);

            var sum = 0;
            void Sum(int count = 1000) { for (int i = 0; i < count; i++) sum += i; }

            var increment = Node.All(
                Node.Run((Entity entity, ref OnInitialize _) => Sum()),
                Node.Run((Entity entity, ref Time _) => Sum()),
                Node.Run((Entity entity, ref Position position) => position.Value.X++),
                Node.Run((Entity entity, ref OnFinalize _) => Sum()),
                Node.Run((Entity entity, ref Velocity velocity) => velocity.Value.X++),
                Node.Destroy(Matcher.True)
            // Node.Run((ref Position position, in Velocity velocity) => position.Value += velocity.Value)
            // Run((in Time time, ref Position position, in Velocity velocity) =>
            //     position.Value += velocity.Value * time.Delta)
            ).Schedule(world);

            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 2500; i++)
            {
                if ((i % 100) == 0)
                {
                    Console.WriteLine($"Iteration {i}: {watch.Elapsed} | {world.Count}/{world.Capacity}");
                    watch.Restart();
                }
                populate();
                increment();
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

    public readonly struct Resource<T>
    {
        static readonly Template<Unit> _template = Template.Create().Add(_ => DefaultUtility.Default<T>());

        public ref T Value => ref _store[0];
        readonly T[] _store;

        public Resource(World world)
        {
            var creator = world.Creator(_template, 1);
            if (creator.Segment.Chunks.Length == 0) creator.Create();
            _store = (T[])creator.Segment.Chunks[0].Stores[0];
        }
    }

    public readonly struct State<T>
    {
        static readonly Template<T> _template = Template.Create<T>().Add(state => state);

        public ref T Value => ref _store[_index];
        readonly T[] _store;
        readonly byte _index;

        public State(T value, World world, byte size = 1)
        {
            var creator = world.Creator(_template, size);
            var entity = creator.Create(value);
            world.TryDatum(entity, out var datum);
            _index = (byte)datum.Index;
            _store = (T[])datum.Chunk.Stores[0];
        }
    }

    struct Messages<T> { public ConcurrentQueue<T> Queue; public int Capacity; }

    public readonly struct Emitter<T>
    {
        readonly Segment _segment;
        public Emitter(World world) => _segment = world.Segment(new[] { world.Meta(typeof(Messages<T>)) }, 8);
        public void Emit(in T message)
        {
            foreach (var chunk in _segment.Chunks)
            {
                var store = (Messages<T>[])chunk.Stores[0];
                for (int i = 0; i < chunk.Count; i++)
                {
                    ref var messages = ref store[i];
                    if (messages.Capacity != 0) messages.Queue.Enqueue(message);
                    while (messages.Capacity >= 0 && messages.Queue.Count > messages.Capacity)
                        messages.Queue.TryDequeue(out _);
                }
            }
        }
    }

    public readonly struct Receiver<T>
    {
        readonly State<Messages<T>> _state;
        public Receiver(World world, int capacity = -1) => _state = new(new() { Queue = new(), Capacity = capacity }, world, 8);
        public bool TryReceive(out T message) => _state.Value.Queue.TryDequeue(out message);
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