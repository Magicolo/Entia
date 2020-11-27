using System;
using System.Linq;
using System.Collections.Generic;
using Entia.Check;
using Entia.Core;
using static Entia.Check.Generator;

namespace Entia.Core
{
    public static class Checks
    {
        static readonly Generator<Outcome<object>> _map =
            Types.Make(typeof(Checks).GetMethod(nameof(Map), ReflectionUtility.All), Types.Abstract)
                .Bind(method => (Generator<Outcome<object>>)method.Invoke(null, null))
                .Cache(0.9);

        public static void Run()
        {
            _map.Check(nameof(TypeMap<Unit, Unit>), outcome => outcome.Properties);
        }

        static Generator<Outcome<object>> Map<TKey, TValue>()
        {
            static Mutation<TypeMap<TKey, TValue>> From(string name, Mutate<TypeMap<TKey, TValue>> mutate) =>
                Mutation.From(name, mutate);

            var key = Any(Types.Derived<TKey>(), Types.Type).Cache();
            var set = key.Map(key => From($"Set({key.Format()})", map =>
            {
                var count = map.Count;
                var has = map.Has(key);
                var set = map.Set(key, default);
                return Prove().ToArray();

                IEnumerable<Property> Prove()
                {
                    yield return ("Set", !(set & has));
                    yield return ("Count", set ? map.Count > count : map.Count == count);
                    yield return ("Has(key)", set | has == map.Has(key));
                    yield return ("Has(key, true, true)", set | has == map.Has(key, true, true));
                    yield return ("Has(key, true, false)", set | has == map.Has(key, true, false));
                    yield return ("Has(key, false, true)", set | has == map.Has(key, false, true));
                    yield return ("Has(key, false, false)", set | has == map.Has(key, false, false));
                }
            }));

            var remove = key.Map(key => From($"Remove({key.Format()})", map =>
            {
                var count = map.Count;
                var has = map.Has(key, true, true);
                var remove = map.Remove(key, true, true);
                return Prove().ToArray();

                IEnumerable<Property> Prove()
                {
                    yield return ("Remove", has == remove);
                    yield return ("Count", remove ? map.Count < count : map.Count == count);
                    yield return ("Has(key)", !map.Has(key));
                    yield return ("Has(key, false, false)", !map.Has(key, false, false));
                    yield return ("Has(key, false, true)", !map.Has(key, false, true));
                    yield return ("Has(key, true, false)", !map.Has(key, true, false));
                    yield return ("Has(key, true, true)", !map.Has(key, true, true));
                };
            }));

            var clear = Constant(From("Clear", map =>
            {
                var count = map.Count;
                var clear = map.Clear();
                return Prove().ToArray();

                IEnumerable<Property> Prove()
                {
                    yield return ("Clear", count > 0 == clear);
                    yield return ("Count", clear ? map.Count < count : map.Count == count);
                    yield return ("Empty", map.Count == 0);
                };
            }));

            var clone = Constant(From("Clone", map =>
            {
                var clone = map.Clone();
                return Prove().ToArray();

                IEnumerable<Property> Prove()
                {
                    yield return ("Count", clone.Count == map.Count);
                    yield return ("Keys", clone.Keys.SequenceEqual(map.Keys));
                    yield return ("Values", clone.Values.SequenceEqual(map.Values));
                };
            }));

            return Factory(() => new TypeMap<TKey, TValue>())
                .Mutate(Any((50f, set), (25f, remove), (1f, clear), (1f, clone)).Repeat(Range(1000)))
                .Map(outcome =>
                {
                    return outcome.Add(Prove().ToArray()).Box();

                    IEnumerable<Property> Prove()
                    {
                        var map = outcome.Value;
                        var comparer = EqualityComparer<TValue>.Default;
                        foreach (var property in outcome.Properties) yield return property;

                        yield return ("Enumerator.Count", map.Count == map.Count());
                        yield return ("Keys.Count", map.Count == map.Keys.Count());
                        yield return ("Values.Count", map.Count == map.Values.Count());

                        foreach (var pair in map)
                        {
                            yield return ("TryIndex", map.TryIndex(pair.key, out var index));
                            yield return ("Indices", map.Indices(pair.key).Contains(index));
                            yield return ("Indices(true, true)", map.Indices(pair.key, true, true).Contains(index));
                            yield return ("Indices(true, false)", map.Indices(pair.key, true, false).Contains(index));
                            yield return ("Indices(false, true)", map.Indices(pair.key, false, true).Contains(index));
                            yield return ("Indices(false, false)", map.Indices(pair.key, false, false).Contains(index));

                            yield return ("Has(key)", map.Has(pair.key));
                            yield return ("Has(key, true, true)", map.Has(pair.key, true, true));
                            yield return ("Has(key, true, false)", map.Has(pair.key, true, false));
                            yield return ("Has(key, false, true)", map.Has(pair.key, false, true));
                            yield return ("Has(key, false, false)", map.Has(pair.key, false, false));
                            yield return ("Has(index)", map.Has(index));
                            yield return ("Has(index, true, true)", map.Has(index, true, true));
                            yield return ("Has(index, true, false)", map.Has(index, true, false));
                            yield return ("Has(index, false, true)", map.Has(index, false, true));
                            yield return ("Has(index, false, false)", map.Has(index, false, false));

                            yield return ("TryGet(key)", map.TryGet(pair.key, out var value) && comparer.Equals(pair.value, value));
                            yield return ("TryGet(key, true, true)", map.TryGet(pair.key, out value, true, true) && comparer.Equals(pair.value, value));
                            yield return ("TryGet(key, true, false)", map.TryGet(pair.key, out value, true, false) && comparer.Equals(pair.value, value));
                            yield return ("TryGet(key, false, true)", map.TryGet(pair.key, out value, false, true) && comparer.Equals(pair.value, value));
                            yield return ("TryGet(key, false, false)", map.TryGet(pair.key, out value, false, false) && comparer.Equals(pair.value, value));
                            yield return ("TryGet(index)", map.TryGet(index, out value) && comparer.Equals(pair.value, value));
                            yield return ("TryGet(index, true, true)", map.TryGet(index, out value, true, true) && comparer.Equals(pair.value, value));
                            yield return ("TryGet(index, true, false)", map.TryGet(index, out value, true, false) && comparer.Equals(pair.value, value));
                            yield return ("TryGet(index, false, true)", map.TryGet(index, out value, false, true) && comparer.Equals(pair.value, value));
                            yield return ("TryGet(index, false, false)", map.TryGet(index, out value, false, false) && comparer.Equals(pair.value, value));

                            yield return ("Get(key)", comparer.Equals(map.Get(pair.key, out var success), pair.value) && success);
                            yield return ("Get(key, true, true)", comparer.Equals(map.Get(pair.key, out success, true, true), pair.value) && success);
                            yield return ("Get(key, true, false)", comparer.Equals(map.Get(pair.key, out success, true, false), pair.value) && success);
                            yield return ("Get(key, false, true)", comparer.Equals(map.Get(pair.key, out success, false, true), pair.value) && success);
                            yield return ("Get(key, false, false)", comparer.Equals(map.Get(pair.key, out success, false, false), pair.value) && success);
                            yield return ("Get(index)", comparer.Equals(map.Get(index, out success), pair.value) && success);
                            yield return ("Get(index, true, true)", comparer.Equals(map.Get(index, out success, true, true), pair.value) && success);
                            yield return ("Get(index, true, false)", comparer.Equals(map.Get(index, out success, true, false), pair.value) && success);
                            yield return ("Get(index, false, true)", comparer.Equals(map.Get(index, out success, false, true), pair.value) && success);
                            yield return ("Get(index, false, false)", comparer.Equals(map.Get(index, out success, false, false), pair.value) && success);
                        }
                    }
                });
        }

        static Failure<T>[] Check<T>(this Generator<T> generator, string name, Prove<T> prove) =>
            generator.Prove(prove).Log(name).Check();
    }
}