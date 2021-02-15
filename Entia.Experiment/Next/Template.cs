using System;
using System.Collections.Generic;
using System.Linq;
using Entia.Core;

namespace Entia.Experiment.V4
{
    public readonly struct Template<T>
    {
        public delegate void Initialize(int index, int count, Array store, in T state);
        public static readonly Template<T> Empty = new(Array.Empty<(Type, Initialize)>());
        public static implicit operator Template<T>(Template<Unit> template) => template.Adapt<T>();

        public readonly (Type type, Initialize initialize)[] Initializers;
        public Template(params (Type type, Initialize initialize)[] initializers) { Initializers = initializers; }
        public Template<T> Add<TComponent>() => this.Add(typeof(TComponent));
        public Template<T> Remove<TComponent>() => this.Remove(typeof(TComponent));
        public Template<TTarget> Adapt<TTarget>(Func<TTarget, T> adapt) => this.Adapt<T, TTarget>(adapt);
    }

    public static class Template
    {
        public static Template<T> Empty<T>() => Template<T>.Empty;
        public static Template<Unit> Empty() => Empty<Unit>();

        public static Template<Unit> Adapt<T>(this Template<T> template, T state) => template.Adapt<T, Unit>(_ => state);
        public static Template<T> Adapt<T>(this Template<Unit> template) => template.Adapt<Unit, T>(_ => default);
        public static Template<TTarget> Adapt<TSource, TTarget>(this Template<TSource> template, Func<TTarget, TSource> adapt) =>
            new(template.Initializers.Select(pair => (pair.type,
                new Template<TTarget>.Initialize((int index, int count, Array store, in TTarget state) =>
                    pair.initialize(index, count, store, adapt(state))))));

        public static Template<T> All<T>(params Template<T>[] templates) => templates.All();
        public static Template<T> All<T>(this IEnumerable<Template<T>> templates) => new(templates
            .SelectMany(template => template.Initializers)
            .GroupBy(pair => pair.type)
            .Select(group => group.Last())
            .ToArray());

        public static Template<T> Add<T>(this Template<T> template, Type type) => template.Add(new[] { type });
        public static Template<T> Add<T>(this Template<T> template, params Type[] types) =>
            new(template.Remove(types).Initializers.Append(types.Select(type =>
                (type, new Template<T>.Initialize((int index, int count, Array store, in T state) => { })))));

        public static Template<T> Add<T, TComponent>(this Template<T> template, TComponent component) =>
            new(template.Remove<TComponent>().Initializers.Append(
                (typeof(TComponent), new((int index, int count, Array store, in T state) =>
                    Array.Fill((TComponent[])store, component, index, count)))));

        public static Template<T> Add<T, TComponent>(this Template<T> template, Func<T, TComponent> provide) =>
            new(template.Remove<TComponent>().Initializers.Append(
                (typeof(TComponent), new((int index, int count, Array store, in T state) =>
                {
                    var casted = (TComponent[])store;
                    for (int i = 0; i < count; i++) casted[i + index] = provide(state);
                }))));

        public static Template<T> Add<T>(this Template<Unit> template, Template<T> other) => template.Add(new[] { other });
        public static Template<T> Add<T>(this Template<Unit> template, params Template<T>[] others) =>
            All(others.Prepend(template));
        public static Template<T> Add<T>(this Template<T> template, Template<T> other) => template.Add(new[] { other });
        public static Template<T> Add<T>(this Template<T> template, params Template<T>[] others) =>
            All(others.Prepend(template));

        public static Template<T> Remove<T>(this Template<T> template, params Type[] types) =>
            new(template.Initializers.Where(pair => !types.Contains(pair.type)).ToArray());
        public static Template<TSource> Remove<TSource, TTarget>(this Template<TSource> template, params Template<TTarget>[] templates) =>
            template.Remove(templates.Select(template => template.Initializers.Select(pair => pair.type)).Flatten());
    }
}