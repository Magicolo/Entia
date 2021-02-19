using System;
using System.Collections.Generic;
using System.Linq;
using Entia.Core;

namespace Entia.Experiment.V4
{
    public sealed class Cache<T>
    {
        T _value;
        readonly Func<T, Option<T>> _update;
        public Cache(T value, Func<T, Option<T>> update) { _value = value; _update = update; }
        public bool TryGet(out T value) { value = Get(out var changed); return changed; }
        public Option<T> TryGet() => TryGet(out var value) ? value : Option.None();
        public T Get() => Get(out _);
        public T Get(out bool changed)
        {
            changed = _update(_value).Set(ref _value);
            return _value;
        }
    }

    public static class Cache
    {
        static class Array<T>
        {
            public static readonly Cache<T[]> Empty = Constant(Array.Empty<T>());
        }

        public static Cache<T> Create<T>(T value, Func<T, Option<T>> update) => new(value, update);
        public static Cache<T> Variable<T>(Func<T> provide) => Create(provide(), _ => provide());
        public static Cache<T> Constant<T>(T value) => Create(value, _ => Option.None());
        public static Cache<T[]> Empty<T>() => Array<T>.Empty;

        public static Cache<T> Update<T>(this Cache<T> cache, Func<T, T, Option<T>> update) =>
            Create(cache.Get(), value => update(cache.Get(), value));
        public static Cache<TTarget> Update<TSource, TTarget>(this Cache<TSource> cache, TTarget value, Func<TSource, TTarget, Option<TTarget>> update) =>
            Create(value, value => update(cache.Get(), value));

        public static Cache<TTarget> Map<TSource, TTarget>(this Cache<TSource> cache, Func<TSource, TTarget> map) =>
            Create(map(cache.Get()), _ => cache.TryGet().Map(map));

        public static Cache<T> Change<T>(Func<T> provide, Func<T, T, bool> equals = null)
        {
            equals ??= EqualityComparer<T>.Default.Equals;
            return Create(provide(), previous =>
                provide() is var current && equals(previous, current) ?
                Option.None() : current);
        }

        public static Cache<T> Change<T>(this Cache<T> cache, Func<T, T, bool> equals = null)
        {
            equals ??= EqualityComparer<T>.Default.Equals;
            return Create(cache.Get(), previous =>
                cache.Get(out var changed) is var current && !changed && equals(previous, current) ?
                Option.None() : current);
        }

        public static Cache<TTarget> Change<TSource, TTarget>(this Cache<TSource> cache, Func<TSource, TTarget> map, Func<TSource, TSource, bool> equals = null) =>
            cache.Change(equals).Map(map);

        public static Cache<T[]> Any<T>(this IEnumerable<Cache<T>> caches) => Any(caches.ToArray());
        public static Cache<T[]> Any<T>(params Cache<T>[] caches) =>
            Create(caches.Select(cache => cache.Get()), values =>
            {
                var changed = false;
                for (int i = 0; i < caches.Length; i++) changed |= caches[i].TryGet().Set(ref values[i]);
                return changed ? values : Option.None();
            });

        public static Cache<(TLeft, TRight)> Or<TLeft, TRight>(this Cache<TLeft> left, Cache<TRight> right) =>
            Create((left.Get(), right.Get()), pair =>
                left.TryGet(out var leftValue) ? (leftValue, pair.Item2) :
                right.TryGet(out var rightvalue) ? (pair.Item1, rightvalue) :
                Option.None());
    }
}