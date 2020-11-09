using System;
using System.Linq;
using Entia.Check;
using Entia.Core;
using static Entia.Check.Generator;

namespace Entia.Corez
{
    public readonly struct Outcome<T>
    {
        public readonly T Value;
        public readonly Action<T>[] Actions;
        public readonly Property[] Properties;

        public Outcome(T value, Action<T>[] actions, Property[] properties)
        {
            Value = value;
            Actions = actions;
            Properties = properties;
        }

        public override string ToString() => string.Join(", ", Actions);
    }

    public delegate Property[] Act<T>(T value);
    public readonly struct Action<T>
    {
        public readonly string Name;
        public readonly Act<T> Act;
        public Action(string name, Act<T> act) { Name = name; Act = act; }
        public override string ToString() => Name;
    }

    public static class Action
    {
        public static Action<T> From<T>(string name, Act<T> act) => new Action<T>(name, act);
        public static Action<T> From<T>(Act<T> act) => From(act.Method.Format(), act);
    }

    public static class Checks
    {
        public static void Run()
        {
            Check<object, object>();
            Check<ValueType, object>();
        }

        static void Check<TKey, TValue>()
        {
            static Action<TypeMap<TKey, TValue>> From(string name, Act<TypeMap<TKey, TValue>> act) => Action.From(name, act);
            var key = Types.Type.Cache();
            var set = key.Map(key => From($"Set({key.Format()})", map =>
            {
                var count = map.Count;
                var has = map.Has(key);
                var set = map.Set(key, default);
                return new Property[]
                {
                    ("Set", !(set & has)),
                    ("Count", set ? map.Count > count : map.Count == count),
                    ("Has(key)", set | has == map.Has(key)),
                    ("Has(key, true, true)", set | has == map.Has(key, true, true)),
                    ("Has(key, true, false)", set | has == map.Has(key, true, false)),
                    ("Has(key, false, true)", set | has == map.Has(key, false, true)),
                    ("Has(key, false, false)", set | has == map.Has(key, false, false)),
                };
            }));
            var remove = key.Map(key => From($"Remove({key.Format()})", map =>
            {
                var count = map.Count;
                var has = map.Has(key, true, true);
                var remove = map.Remove(key, true, false);
                return new Property[]
                {
                    ("Remove", has == remove),
                    ("Count", remove ? map.Count < count : map.Count == count),
                    ("Has(key)", !map.Has(key)),
                    ("Has(key)", !map.Has(key, false, false)),
                    ("Has(key)", !map.Has(key, false, true)),
                    ("Has(key)", !map.Has(key, true, false)),
                    ("Has(key)", !map.Has(key, true, true)),
                };
            }));
            var clear = Constant(From("Clear", map =>
            {
                var count = map.Count;
                var clear = map.Clear();
                return new Property[]
                {
                    ("Clear", count > 0 == clear),
                    ("Count", clear ? map.Count < count : map.Count == count),
                    ("Empty", map.Count == 0),
                };
            }));
            var clone = Constant(From("Clone", map =>
            {
                var clone = map.Clone();
                return new Property[]
                {
                    ("Count", clone.Count == map.Count),
                    ("Keys", clone.Keys.SequenceEqual(map.Keys)),
                    ("Values", clone.Values.SequenceEqual(map.Values)),
                };
            }));

            Factory(() => new TypeMap<TKey, TValue>()).Check(Any(
                (50f, set),
                (25f, remove),
                (1f, clear),
                (1f, clone)));
        }

        static Failure<Outcome<T>>[] Check<T>(this Generator<T> generator, Generator<Action<T>> action) =>
            generator.Check(action.Repeat(Range(1, 1000)));
        static Failure<Outcome<T>>[] Check<T>(this Generator<T> generator, Generator<Action<T>[]> actions) =>
            generator.And(actions)
                .Map(pair => new Outcome<T>(pair.Item1, pair.Item2, pair.Item2
                    .Select(action => action.Act(pair.Item1))
                    .Flatten()))
                .Prove(tuple => tuple.Properties)
                .Log(typeof(T).Format())
                .Check();
    }
}