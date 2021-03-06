using System;
using System.Drawing;
using System.Linq;
using System.Numerics;
using Entia.Bench;
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
    */

    /*
    TODO:
    - Add the 'On<T>' node which executes a wrapped node conditionally to the reception of a message of type 'T'.
    - Allow segments to defragment and resize their chunks.
        - When a segment has many chunks, it may resize the size of the chunks and move entities to make it more
        compact and/or contiguous.
        - This could be automatically scheduled such that it happens in parallel to other systems, or it can be
        scheduled at the start/end of a frame.
        - The operation could be cancelled if the other parallel tasks are completed before it, and then it could
        be continued on the next frame. This would require the task to frequently check a cancellation token.
        - Increasing the size of chunks could be triggered when 'chunkCount / chunkSize > processors'.
        - This operation will have a 'Write' dependency on everything in the segment.
    - Relationship data will need to be stored in 'World.Datum' or in a component.
        - 'World.Datum' allows to reuse the 'Children' array.
        - As a 'World.Datum', it would be initialized in 'World.Create'.

        - A component is the most obvious place to put it.
        - As a component, it would be initialized as a 'Template<T>.Initialize'.
        - When querying, components will be contiguous in memory while 'World.Datum' will not.

    - Allow 'world.Create' to create a hierarchy of entities in a batch.
        - Ex: A template describes a hierarchy of 13 entities and we would want to instantiate 8 of these. Then
        the world could reserve 8 * 13 entities in a single operation and then initialize them.
        - If some entities of the hierarchy would be allocated in the same segment, they should be grouped and
        initialized together; possibly under the same lock.

    - Static entity hierarchies:
        - Entities in a segment would all have a parent that belongs to the same segment.
        - This means that the parent segment can be directly stored in each segment.
        - Allows matchers such as:
            - 'Matcher.Root()' -> matches segments that do not have a parent.
            - 'Matcher.Parent(...)' -> matches segments that have a parent that matcher the inner matcher.
            - 'Matcher.Ancestor(...)' -> matches segments that do not have a parent.
        - Note that segments will be more fragmented because of the parent constraint.
        - Note that in order to retrieve the component of a parent, a dynamic fetch must be done for each entity.
        even if the fetch is garanteed to succeed.
            if (world.TryData(entity, out var data) &&
                world.TryData(data.Parent, out data))
                return ref ((T[])store)[data.Index];
    - Dynamic entity hierarchies:
        - 'Segment.TryStore' will become a very frequent operation. May require to reintroduce the 'indices' array in segments.
            if (world.TryData(entity, out var data) &&
                world.TryData(data.Parent, out data) &&
                data.Segment.TryStore(meta, out var store))
                return ref ((T[])store)[data.Index];

    TODO:
    - Allow plans to be reordered to allow for more parallelism.
    - Figure out a messaging mechanism.
    - Make a source generator for the 'Run' functions.
    - Test adding segments between frames.

    FIX:
    - Oh oh: `Node.Destroy(destroyer => Node.Create(Template.Empty().Add((entity, _) => destroyer.Destroy(entity)), 100))`
    - There's a rare multi-threading bug where some entities fail to be destroyed.
        - Most likely related with a buffer resize.
    - Create/Destroy operations can have unpredictable (but thread-safe) effects when they affect a segment
    that is being iterated on.
        - Create could cause the iteration to visit or not the newly created entity.
        - Destroy could cause an entity to not be visited since it has been swapped.







    public enum Characters { None, Octopus = 1 << 0 }
    struct Weapon
    {
        public Characters UsableBy;
    }
    struct Physics
    {
        struct Body { public bool Active; }
    }
    struct Character
    {
        public struct Arm { ... }
        public Characters Kind;
    }

    INode EquipWeapon =
        Node.Families(families =>
        Node.Query(All(Read<Weapon>(), Write<Physics.Body>(), Root())), queryWeapons =>
        Node.Query(Children(All(Has<Character.Arm>(), Not(Child(Has<Weapon>())))), queryArms =>
        Node.Run((in Physics physics, in Physics.Body body, in Character character) =>
        {
            foreach (var weaponItem in physics.Collisions(body, queryWeapons))
            {
                if (weaponItem.Weapon.UsableBy.HasAll(character.Kind))
                {
                    foreach (var armItem in queryArms)
                    {
                        weaponItem.Body.Active = false;
                        families.Adopt(weaponItem.Entity, armItem.Entity);
                        break;
                    }
                }
            }
        }))));

    Template<Unit> Octopus = Template.Empty()
        .Add(Enumerable.Repeat(Template.Empty(), 8), (arms, _) => new Character { Arms = arms.ToArray() })
        .Adopt(Enumerable.Repeat(Template.Empty(), 8))
        .Add((_, _, children, _) => new Character { Arms = children.Slice(0, 8).ToArray() });
    ;
    INode EquipWeapon =
        Node.Inject((Entities entities) =>
        Node.Query(Write<Character.Arm>(), queryArms =>
        Node.Query(All(Write<Weapon>(), Write<Physics.Body>())), queryWeapons =>
        Node.Run((in Physics physics, Entity entity, in Physics.Body body, in Character character) =>
        {
            foreach (var weaponItem in physics.Collisions(body, queryWeapons))
            {
                if (weaponItem.Weapon.UsableBy.HasAll(character.Kind) && !entities.Has(weaponItem.Weapon.Owner))
                {
                    foreach (var arm in character.Arms)
                    {
                        if (queryArms.TryGet(arm, out var armItem) && !entities.Has(armItem.Arm.Weapon))
                        {
                            armItem.Arm.Weapon = weaponItem.Entity;
                            weaponItem.Weapon.Owner = entity;
                            weaponItem.Body.Active = false;
                            break;
                        }
                    }
                }
            }
        }))));
    */

    public static class Test
    {
        public struct OnInitialize { }
        public struct OnFinalize { }

        public struct Game { public bool Quit; }
        public struct Body { public Entity Head, Torso, Abdomen; }
        public struct Target { public Entity Value; }
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

        public static readonly Template<Unit> Head = Head.Create(nameof(Head));
        public static readonly Template<Unit> Torso = Torso.Create(nameof(Torso));
        public static readonly Template<Unit> Abdomen = Abdomen.Create(nameof(Abdomen));
        public static readonly Template<Unit> Insect =
            Insect.Create(nameof(Insect))
                .Adopt(Head, Torso, Abdomen)
                .Add((_, _, children, _) => new Body { Head = children[0], Torso = children[1], Abdomen = children[2] });

        public static readonly Template<(Vector2 position, double angle)> Player =
            Player.Create(nameof(Player))
                .Add(Physical(default, 0.25))
                .Add(new Sprite { Path = "Shapes/Triangle", Color = Color.Cyan })
                .Add(new Health { Current = 1.0, Maximum = 1.0, Damageable = DamageKinds.Body })
                .Add(new Motion { MoveSpeed = 0.0, RotateSpeed = 2.0 })
                .Add<Controller>();

        public static readonly Template<(Vector2 position, double angle, double lifetime, double speed, double health)> Enemy =
            Enemy.Create(nameof(Enemy))
                .Add(Physical(new(3f, 3f), 1.5), state => (state.position, state.angle))
                .Add(state => new Sprite { Path = "Shapes/Square", Color = Color.FromArgb(255, 255, (int)(255 - state.health * 75), 0) })
                .Add(state => new Health { Current = state.health, Maximum = state.health, Damageable = DamageKinds.Bullet })
                .Add(state => new Lifetime { Duration = state.lifetime })
                .Add(state => new ForwardMotion { Speed = state.speed })
                .Add(new Weapon { Kind = DamageKinds.Body, Amount = 1.0 })
                .Add<IsObservable>();

        public static readonly Template<(Vector2 position, double angle, double speed)> Bullet =
            Bullet.Create(nameof(Bullet))
                .Add(Physical(new(0.5f, 0.5f), 0.1), state => (state.position, state.angle))
                .Add(new Sprite { Path = "Shapes/Circle", Color = Color.Yellow })
                .Add(new Health { Current = 1.0, Maximum = 1.0, Damageable = DamageKinds.Body })
                .Add(new Lifetime { Duration = 3.0 })
                .Add(state => new ForwardMotion { Speed = state.speed })
                .Add(new Weapon { Kind = DamageKinds.Bullet, Amount = 1.0 });

        static Template<T> Create<T>(this Template<T> _, string name) => Create<T>(name);
        static Template<T> Create<T>(string name) => Template.Empty().Add(new Debug { Name = name });

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

        static readonly Type[] _types = typeof(Test).GetNestedTypes().Where(type => type.IsValueType).ToArray();
        static readonly Random _random = new();
        static Template<Unit> Random(int depth = 12)
        {
            var template = Template.Empty();
            while (_random.NextDouble() < 0.95) template = template.Add(_types[_random.Next(_types.Length)]);
            while (_random.NextDouble() < 8.0 / depth) template = template.Adopt(Random(depth + 1));
            return template;
        }

        public static void Do()
        {
            var world = new World();
            var entities = world.Entities();
            // var game = world.Resource<Game>();

            var value = 0L;
            void Sum(Entity entity) { for (int i = 0; i < 1_000; i++) value += entity.Identifier; }

            while (true)
            {
                var templates = new[] { Player.Adapt(default), Enemy.Adapt(default), Bullet.Adapt(default), Insect, Head, Torso, Abdomen }
                    .Concat(Enumerable.Range(0, 17).Select(_ => Random()))
                    .ToArray();
                templates.Shuffle();

                var tests = Enumerable.Range(5, 5)
                    .Select(index => Math.Pow(2, index))
                    .Select(size => (size, node: Node.All(
                        templates.Select((template, count) => Node.Create(template.Size((int)size), count + 1)).All(),
                        Node.Run(Sum),
                        Node.Destroy(Matcher.True))))
                    .SelectMany(pair => new[]
                    {
                        new Bencher.Test($"Sequential{pair.size}", pair.node.Synchronous().Schedule(new())),
                        new Bencher.Test($"Parallel{pair.size}", pair.node.Schedule(new()))
                    })
                    .ToArray();
                Bencher.Measure(tests[0], tests, 100, 1);
            }

            /*
            Node.Run(
                // Iterates over all entities that have a 'Position', a 'Velocity' and a parent with a 'Motion'.
                (ref Position position, in Velocity velocity, in Parent<Motion> motion) =>
                {
                    if (motion.Value.Enabled) position.Value += velocity.Value;
                },
                // Matches entities that have a parent with a component 'CanMove'.
                Matcher.Parent(Matcher.Has<CanMove>()));

            static void Run(ref Position position, in Velocity velocity, [Matcher.Parent] in Motion motion)
            {
                if (motion.Value.Enabled) position.Value += velocity.Value;
            }
            Node.Run(Run, Matcher.Parent(Matcher.Has<CanMove>()));

            Node.Run(
                // Iterates over all entities that have a 'Velocity' and at least 1 child with a 'Position'.
                (in Velocity velocity, ref Children<Position> children) =>
                {
                    foreach (ref var position in children)
                        position.Value += velocity.Value
                },
                // Matches entities that have no parent.
                Matcher.Root());

            // Has type 'Query<And<Read<Velocity>, Write<Position>>>'.
            Node.Query(Query.And(Query.Entity, Query.Read<Velocity>(), Query.Write<Position>()), query1 =>
            // Has type 'Query<Write<Target>>'.
            Node.Query(Query.Write<Target>(), query2 =>
                Node.Run(() =>
                {
                    // 'item1' has a generated type.
                    foreach (var item1 in query1)
                        // 'item1.Entity' has type 'Entity<(Velocity, Position)>'
                        // 'item2' has a generated type.
                        if (query2.TryGet(item1.Entity, out var item2))
                            item2.Target.Value = item1.Entity;
                })));

            // Shorter version of a query.
            Node.Run(
                Query.And(Query.Read<Velocity>(), Query.Write<Position>()),
                item => item.Position.Value += item.Velocity.Value);

            // Shortest version of a query.
            Node.Run((in Velocity velocity, ref Position position) => position.Value += velocity.Value);

            Node.Run(
                // Bottom up query.
                Query.And(Query.Parent(Query.Read<Velocity>()), Query.Write<Position>()),
                // Add the velocity of the parent to its immediate children.
                item => item.Position.Value += item.Velocity.Value);
            Node.Run(
                // Top down query.
                Query.And(Query.Read<Velocity>(), Query.Children(Query.Write<Position>())),
                // 'item.Positions' has a generated 'ref struct IEnumerable<...>' type.
                item =>
                {
                    foreach (var child in item.Positions)
                        child.Position.Value += item.Velocity.Value;
                });

            Node.Run(
                // The 'bool' in 'Ancestors' and 'Descendants' specifies to include 'self' or not.
                Query.And(
                    Query.Root(Query.Has<Motion>()),
                    Query.Ancestors(Query.Read<Velocity>(), false),
                    Query.Descendants(Query.Write<Position>(), true)),
                // 'item.Velocities' and 'item.Positions' have a generated 'ref struct IEnumerable<...>' type.
                item =>
                {
                    // Each ancestor that has a 'Velocity'.
                    foreach (var ancestor in item.Velocities)
                        // Each descendant that has a 'Position'.
                        foreach (var descendant in item.Positions)
                            descendant.Position.Value += ancestor.Value;
                });
            */
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

        public State(T value, World world, int size = 1)
        {
            var creator = world.Creator(_template.Size(size));
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