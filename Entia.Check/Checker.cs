using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Entia.Core;

namespace Entia.Check
{
    public sealed class Checker<T>
    {
        public readonly Generator<T> Generator;
        public readonly Prover<T> Prover;
        public readonly int Iterations = 1_000 * Environment.ProcessorCount;
        public readonly int Parallel = Environment.ProcessorCount;
        public readonly Action OnPre = () => { };
        public readonly Action<Failure<T>[]> OnPost = _ => { };
        public readonly Action<double> OnProgress = _ => { };

        public Checker(Generator<T> generator, Prover<T> prover) { Generator = generator; Prover = prover; }

        Checker(Generator<T> generator, Prover<T> prover, int iterations, int parallel, Action onPre, Action<Failure<T>[]> onPost, Action<double> onProgress)
        {
            Generator = generator;
            Prover = prover;
            Iterations = iterations;
            Parallel = parallel;
            OnPre = onPre;
            OnPost = onPost;
            OnProgress = onProgress;
        }

        public Checker<T> With(Generator<T>? generator = null, Prover<T>? prover = null, int? iterations = null, int? parallel = null, Action onPre = null, Action<Failure<T>[]> onPost = null, Action<double> onProgress = null) =>
            new Checker<T>(generator ?? Generator, prover ?? Prover, iterations ?? Iterations, parallel ?? Parallel, OnPre + onPre, OnPost + onPost, OnProgress + onProgress);
    }

    public static class Checker
    {
        public static Checker<T> Prove<T>(this Generator<T> generator, string name, Func<T, bool> prove) =>
            generator.Prove(value => new Property(name, prove(value)));
        public static Checker<T> Prove<T>(this Generator<T> generator, Func<T, Property> prove) =>
            generator.Prove(value => new[] { prove(value) });
        public static Checker<T> Prove<T>(this Generator<T> generator, Prove<T> prove) =>
            generator.Prove(new Prover<T>(prove));
        public static Checker<T> Prove<T>(this Generator<T> generator, Prover<T> prover) =>
            new Checker<T>(generator, prover);

        public static Checker<T> Log<T>(this Checker<T> checker, string name = null)
        {
            name ??= checker.Generator.Name;
            return checker.With(
                onPre: () =>
                {
                    Console.CursorVisible = false;
                    Console.WriteLine();
                },
                onProgress: progress =>
                {
                    Console.CursorLeft = 0;
                    Console.Write($"Checking '{name}' {checker.Iterations / checker.Parallel}x{checker.Parallel}... {progress * 100:0.00}%");
                },
                onPost: failures =>
                {
                    Console.CursorVisible = true;
                    if (failures.Length > 0)
                        Console.WriteLine($"{string.Join("", failures.Select(failure => $"{Environment.NewLine}-> Property '{failure.Property}' was disproved with value '{failure.Shrinked}'"))}");
                });
        }

        public static Failure<T>[] Check<T>(this Checker<T> checker)
        {
            var progress = new double[checker.Parallel];
            var task = Task.WhenAll(Enumerable.Range(0, checker.Parallel)
                .Select(index => Task.Run(() => Run(checker, index, progress))));

            checker.OnPre();
            var last = 0.0;
            checker.OnProgress(progress.Average());
            while (!task.IsCompleted) if (last.Change(progress.Average())) checker.OnProgress(last);
            checker.OnProgress(progress.Average());
            var results = task.Result.Choose().ToArray();
            checker.OnPost(results);
            return results;

            static Option<Failure<T>> Run(Checker<T> checker, int index, double[] progress)
            {
                var iterations = checker.Iterations / checker.Parallel;
                var maximum = iterations * 0.9; // 10% of tests will have a size of 1.
                var random = new Random();
                for (var i = 0; i <= iterations; i++)
                {
                    progress[index] = i / (double)iterations;
                    var seed = random.Next() ^ Thread.CurrentThread.ManagedThreadId ^ i ^ index ^ Environment.TickCount;
                    var size = Math.Min(i / maximum, 1.0);
                    var state = new Generator.State(size, 0, new Random(seed));
                    var (value, shrinker) = checker.Generator.Generate(state);
                    foreach (var property in checker.Prover.Prove(value))
                    {
                        if (property.Proof) continue;
                        return Shrink(checker, value, shrinker, property, i, seed, size);

                    }
                }
                return Option.None();
            }

            static Failure<T> Shrink(Checker<T> checker, T original, Shrinker<T> shrinker, Property disproved, int iteration, int seed, double size)
            {
                var shrinked = original;
                var @continue = true;
                while (@continue.Change(false))
                {
                    foreach (var generator in shrinker.Shrink())
                    {
                        var state = new Generator.State(size, 0, new Random(seed));
                        var pair = generator.Generate(state);
                        foreach (var property in checker.Prover.Prove(pair.value))
                        {
                            if (property.Proof) continue;
                            (shrinked, shrinker) = pair;
                            disproved = property;
                            @continue = true;
                            break;
                        }
                        if (@continue) break;
                    }
                }
                return new Failure<T>(original, shrinked, disproved, iteration, seed, size);
            }
        }
    }
}