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

        public static Cache<T> Constant<T>(T value) => new(value, _ => Option.None());
        public static Cache<T[]> Empty<T>() => Array<T>.Empty;

        public static Cache<T> Change<T>(this Cache<T> cache, Func<T, T, bool> equals = null) =>
            cache.Change(value => value, equals);
        public static Cache<T> Change<T>(Func<T> provide, Func<T, T, bool> equals = null) =>
            Constant(provide()).Change(_ => provide(), equals);
        public static Cache<TTarget> Change<TSource, TTarget>(TSource value, Func<TSource, TTarget> change, Func<TTarget, TTarget, bool> equals = null) =>
            Constant(value).Change(change, equals);
        public static Cache<TTarget> Change<TSource, TTarget>(this Cache<TSource> cache, Func<TSource, TTarget> change, Func<TTarget, TTarget, bool> equals = null) =>
            cache.Change(change(cache.Get()), (value, _) => change(value), equals);
        public static Cache<TTarget> Change<TSource, TTarget>(this Cache<TSource> cache, TTarget value, Func<TSource, TTarget, TTarget> change, Func<TTarget, TTarget, bool> equals = null)
        {
            equals ??= EqualityComparer<TTarget>.Default.Equals;
            return cache.Change(value, (source, target) =>
                change(source, target) is var value &&
                equals(target, value) ? Option.None() : value);
        }

        public static Cache<T> Change<T>(T value, Func<T, T, Option<T>> change) => Constant(value).Change(change);
        public static Cache<T> Change<T>(this Cache<T> cache, Func<T, T, Option<T>> change) => cache.Change(cache.Get(), change);
        public static Cache<TTarget> Change<TSource, TTarget>(this Cache<TSource> cache, TTarget value, Func<TSource, TTarget, Option<TTarget>> change) =>
            new(value, previous =>
            {
                var value = cache.Get(out var changed);
                return change(value, previous).TryValue(out var current) ? current : changed ? previous : Option.None();
            });

        public static Cache<TTarget> Map<TSource, TTarget>(this Cache<TSource> cache, Func<TSource, TTarget> map) =>
            cache.Map(map(cache.Get()), (value, _) => map(value));
        public static Cache<TTarget> Map<TSource, TTarget>(this Cache<TSource> cache, TTarget value, Func<TSource, TTarget, TTarget> map) =>
            new(value, previous => cache.TryGet().Map(previous, map));

        public static Cache<T[]> Any<T>(this IEnumerable<Cache<T>> caches) => caches.ToArray().Any();
        public static Cache<T[]> Any<T>(params Cache<T>[] caches) =>
            Change(caches.Select(cache => cache.Get()), (_, values) =>
            {
                var changed = false;
                for (int i = 0; i < caches.Length; i++) changed |= caches[i].TryGet().Set(ref values[i]);
                return changed ? values : Option.None();
            });

        public static Cache<(TLeft, TRight)> Or<TLeft, TRight>(this Cache<TLeft> left, Cache<TRight> right) =>
            Change((left.Get(), right.Get()), (_, pair) =>
                left.TryGet(out var leftValue) ? (leftValue, pair.Item2) :
                right.TryGet(out var rightvalue) ? (pair.Item1, rightvalue) :
                Option.None());
    }
}