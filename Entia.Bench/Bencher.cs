using System;
using System.Diagnostics;
using System.Linq;
using Entia.Core;

namespace Entia.Bench
{
    public static class Bencher
    {
        public sealed record Test
        {
            public readonly string Name;
            public readonly Action Run;
            public Test(string name, Action run) { Name = name; Run = run; }
            public Test(Action run) : this(run.Method.Format(), run) { }
            public override string ToString() => Name;
        }

        public static void Measure(Test @base, Test[] tests, uint iterations, uint warmup = 10, bool progress = true)
        {
            string Justify(object value, int length)
            {
                var format = value.ToString();
                return format + new string(' ', Math.Max(length - format.Length, 0));
            }

            string Format((Test test, long total, long minimum, long maximum) result, double baseTotal)
            {
                var column = 20;
                var name = Justify(result.test.Name, column);
                var total = Justify(TimeSpan.FromTicks(result.total), column);
                var ratio = Justify((result.total / baseTotal).ToString("0.000"), column / 2);
                var average = Justify(TimeSpan.FromTicks(result.total / iterations), column);
                var minimum = Justify(TimeSpan.FromTicks(result.minimum), column);
                var maximum = Justify(TimeSpan.FromTicks(result.maximum), column);
                return $"{name} ->   Total: {total} Ratio: {ratio} Average: {average} Minimum: {minimum} Maximum: {maximum}";
            }


            var runners = tests.Prepend(@base);
            runners.Shuffle();
            var results = new (Test test, long total, long minimum, long maximum)[runners.Length];
            var line = Console.CursorTop;
            var visible = Console.CursorVisible;
            Console.CursorVisible = false;
            for (int i = 0; i < runners.Length; i++)
            {
                var runner = runners[i];
                results[i] = Run(runner, iterations, warmup,
                    progress ? progress => Progress(runner.Name, (progress + i) / runners.Length, line) : default);
                Clear(line);
            }
            Console.CursorVisible = visible;

            var reference = results.FirstOrDefault(result => result.test == @base).total;
            foreach (var result in results.OrderBy(result => result.test.Name))
                Console.WriteLine(Format(result, reference));
            Console.WriteLine();
        }

        public static void Measure(Action @base, Action[] tests, uint iterations, uint warmup = 10) =>
            Measure(new Test(@base), tests.Select(test => new Test(test)), iterations, warmup);

        public static void Measure(string name, Action test, uint iterations, uint warmup = 3) => Measure(new Test(name, test), iterations, warmup);
        public static void Measure(Action test, uint iterations, uint warmup = 3) => Measure(new Test(test), iterations, warmup);
        public static void Measure(Test test, uint iterations, uint warmup = 3)
        {
            var (_, total, minimum, maximum) = Run(test, iterations, warmup);
            Console.WriteLine($"{test.Name} \t->   Total: {TimeSpan.FromTicks(total)} | Average: {TimeSpan.FromTicks(total / iterations)} | Minimum: {TimeSpan.FromTicks(minimum)} | Maximum: {TimeSpan.FromTicks(maximum)}");
        }

        static (Test test, long total, long minimum, long maximum) Run(Test test, uint iterations, uint warmup, Action<double>? progress = default)
        {
            progress ??= _ => { };
            progress(0.0);
            Collect();
            for (var i = 0u; i < warmup; i++) test.Run();
            Collect();

            var total = 0L;
            var minimum = long.MaxValue;
            var maximum = long.MinValue;
            var watch = new Stopwatch();
            for (var i = 0u; i < iterations; i++)
            {
                progress((double)i / iterations);
                watch.Restart();
                test.Run();
                watch.Stop();
                total += watch.ElapsedTicks;
                minimum = Math.Min(minimum, watch.ElapsedTicks);
                maximum = Math.Max(maximum, watch.ElapsedTicks);
            }
            progress(1.0);

            return (test, total, minimum, maximum);
        }

        static void Collect()
        {
            GC.Collect();
            GC.WaitForFullGCComplete();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        static void Clear(int line)
        {
            const string blank = "                                                                                                                                                                                                                                                                ";
            Console.SetCursorPosition(0, line);
            Console.Write(blank);
            Console.SetCursorPosition(0, line);
        }

        static void Progress(string title, double progress, int line)
        {
            var left = Console.CursorLeft;
            var text = $"Running '{title}'... {progress * 100.0:0.00}%";
            Console.SetCursorPosition(0, line);
            Console.Write(text);
            if (left >= text.Length) Console.Write(new string(' ', left - text.Length + 1));
        }
    }
}
