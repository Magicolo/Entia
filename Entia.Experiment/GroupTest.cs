using System;
using System.Linq;
using System.Threading.Tasks;
using Entia.Modules;
using Entia.Queryables;

namespace Entia.Experiment
{
    public static class GroupTest
    {
        public static void Benchmark(int iterations)
        {
            var world = WorldTest.RandomWorld(iterations);
            var group = world.Groups().Get(world.Queriers().Get<All<Write<Position>, Read<Velocity>>>());
            var array = group.ToArray();

            void ArrayIndexer()
            {
                for (int i = 0; i < array.Length; i++)
                {
                    ref var item = ref array[i].item;
                    ref var position = ref item.Value1.Value;
                    ref readonly var velocity = ref item.Value2.Value;
                    position.X += velocity.X;
                }
            }

            void ArrayForeach()
            {
                foreach (var (_, item) in array)
                {
                    ref var position = ref item.Value1.Value;
                    ref readonly var velocity = ref item.Value2.Value;
                    position.X += velocity.X;
                }
            }

            void ArrayParallel()
            {
                System.Threading.Tasks.Parallel.For(0, array.Length, index =>
                {
                    ref var item = ref array[index].item;
                    ref var position = ref item.Value1.Value;
                    ref readonly var velocity = ref item.Value2.Value;
                    position.X += velocity.X;
                });
            }

            void GroupForeach1()
            {
                foreach (var (_, item) in group)
                {
                    ref var position = ref item.Value1.Value;
                    ref readonly var velocity = ref item.Value2.Value;
                    position.X += velocity.X;
                }
            }

            void GroupForeach2()
            {
                foreach (ref readonly var item in group.Items)
                {
                    ref var position = ref item.Value1.Value;
                    ref readonly var velocity = ref item.Value2.Value;
                    position.X += velocity.X;
                }
            }

            void GroupParallel1()
            {
                Parallel.ForEach(group.Segments, segment =>
                {
                    foreach (var (_, item) in segment)
                    {
                        ref var position = ref item.Value1.Value;
                        ref readonly var velocity = ref item.Value2.Value;
                        position.X += velocity.X;
                    }
                });
            }

            void GroupParallel2()
            {
                Parallel.ForEach(group.Segments, segment =>
                {
                    Parallel.For(0, segment.Count, index =>
                    {
                        ref readonly var item = ref segment.Items[index];
                        ref var position = ref item.Value1.Value;
                        ref readonly var velocity = ref item.Value2.Value;
                        position.X += velocity.X;
                    });
                });
            }

            Console.WriteLine($"Size: {array.Length}");
            Test.Measure(
                ArrayForeach,
                new Action[] { ArrayIndexer, ArrayParallel, GroupForeach1, GroupForeach2, GroupParallel1, GroupParallel2 },
                1000,
                after: world.Resolve);
            Console.WriteLine();
        }

        public static void Run()
        {
            const int iterations = 100;
            var world = new World();
            var random = new Random();

            void SetRandom(Entity entity)
            {
                var value = random.NextDouble();
                if (value < 0.333)
                    world.Components().Set(entity, new Position { X = (float)value });
                else if (value < 0.666)
                    world.Components().Set(entity, new Velocity { X = (float)value });
            }

            for (var i = 0; i < iterations; i++)
            {
                var entity = world.Entities().Create();
                SetRandom(entity);
                SetRandom(entity);
            }

            world.Resolve();

            for (var i = 0; i < iterations; i++)
            {
                var entity = world.Entities().Create();
                SetRandom(entity);
                SetRandom(entity);
            }

            var group = world.Groups().Get(world.Queriers().Get<Write<Position>>());
            world.Entities().Clear();

            for (var i = 0; i < iterations; i++)
            {
                var entity = world.Entities().Create();
                SetRandom(entity);
                SetRandom(entity);
            }

            world.Resolve();

            foreach (var (_, item) in group)
            {
                item.Value.Y++;
                item.Value.Z--;
            }

            var items = group.ToArray();
            world.Resolve();

            for (var i = 1; i <= iterations; i++)
            {
                var entity = world.Entities().Create();

                var has1 = world.Components().Has<Position>(entity);
                var add1 = world.Components().Set(entity, new Position { X = i });
                ref var write1 = ref world.Components().Get<Position>(entity);
                write1.X++;

                var add2 = world.Components().Set(entity, new Position { Y = i });
                var has2 = world.Components().Has<Position>(entity);
                world.Resolve();
                var has3 = world.Components().Has<Position>(entity);
                var add3 = world.Components().Set(entity, new Position { Z = i });
                var add4 = world.Components().Set(entity, new Velocity { X = i });
                var get1 = world.Components().Get(entity).ToArray();
                world.Resolve();
                var get2 = world.Components().Get(entity).ToArray();

                ref var write2 = ref world.Components().Get<Position>(entity);
                write2.Y++;

                var has4 = world.Components().Has<Position>(entity);
                var has5 = world.Components().Has<Velocity>(entity);
                var remove1 = world.Components().Remove<Position>(entity);
                world.Resolve();
                var has6 = world.Components().Has<Velocity>(entity);
                ref var write3 = ref world.Components().Get<Velocity>(entity);
                write3.X++;
                var clear1 = world.Components().Clear(entity);
                var get3 = world.Components().Get(entity).ToArray();
                var add5 = world.Components().Set(entity, new Position { X = i + 10 });
                var has7 = world.Components().Has<Position>(entity);
                var has8 = world.Components().Has<Velocity>(entity);
                world.Resolve();

                var get4 = world.Components().Get(entity).ToArray();
                var has9 = world.Components().Has<Velocity>(entity);
                ref var write4 = ref world.Components().Get<Position>(entity);
                write4.Y++;
            }
        }
    }
}