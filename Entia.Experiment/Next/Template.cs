using System;
using System.Collections.Generic;
using System.Linq;
using Entia.Core;

namespace Entia.Experiment.V4
{
    public readonly struct Template<T>
    {
        public static readonly Template<T> Empty = new(
            Array.Empty<Template.Initializer<T>>(),
            Array.Empty<Template<T>>(),
            default);

        public static implicit operator Template<T>(Template<Unit> template) => template.Adapt<T>();

        public readonly int? Size;
        public readonly Template.Initializer<T>[] Initializers;
        public readonly Template<T>[] Children;
        public Template(Template.Initializer<T>[] initializers, Template<T>[] children, int? size)
        {
            Initializers = initializers;
            Children = children;
            Size = size;
        }

        public Template<T> Add<TComponent>() => this.Add(typeof(TComponent));
        public Template<T> Remove<TComponent>() => this.Remove(typeof(TComponent));
        public Template<TTarget> Adapt<TTarget>(Func<TTarget, T> adapt) => this.Adapt<T, TTarget>(adapt);
    }

    public static class Template
    {
        public readonly ref struct Context
        {
            public readonly int Index;
            public readonly int Count;
            public readonly Entity[] Entities;
            public readonly Array Store;
            public readonly ReadOnlySpan<Entity> Parents;

            public Context(int index, int count, Entity[] entities, Array store, ReadOnlySpan<Entity> parents)
            {
                Index = index;
                Count = count;
                Entities = entities;
                Store = store;
                Parents = parents;
            }
        }

        public delegate void Initialize<T>(in Context context, in T state);

        public readonly struct Initializer<T>
        {
            public readonly Type Type;
            public readonly Initialize<T> Initialize;
            public Initializer(Type type, Initialize<T> initialize) { Type = type; Initialize = initialize; }
        }

        public static Template<T> Empty<T>() => Template<T>.Empty;
        public static Template<Unit> Empty() => Empty<Unit>();

        public static Template<T> Size<T>(this Template<T> template, int size) =>
            new(template.Initializers, template.Children, size);

        public static Template<Unit> Adapt<T>(this Template<T> template, T state) => template.Adapt<T, Unit>(_ => state);
        public static Template<T> Adapt<T>(this Template<Unit> template) => template.Adapt<Unit, T>(_ => default);
        public static Template<TTarget> Adapt<TSource, TTarget>(this Template<TSource> template, Func<TTarget, TSource> adapt) => new(
            template.Initializers.Select(initializer => new Initializer<TTarget>(
                initializer.Type,
                (in Context context, in TTarget state) => initializer.Initialize(context, adapt(state)))),
            template.Children.Select(child => child.Adapt(adapt)),
            template.Size);

        public static Template<T> Adopt<T>(this Template<T> template, params Template<T>[] children) =>
            new(template.Initializers, template.Children.Append(children), template.Size);

        public static Template<T> All<T>(params Template<T>[] templates) => templates.All();
        public static Template<T> All<T>(this IEnumerable<Template<T>> templates) => new(
            templates
                .SelectMany(template => template.Initializers)
                .GroupBy(pair => pair.Type)
                .Select(group => group.Last())
                .ToArray(),
            templates.SelectMany(template => template.Children).ToArray(),
            templates.Max(template => template.Size));

        public static Template<T> Add<T>(this Template<T> template, Type type) => template.Add(new[] { type });
        public static Template<T> Add<T>(this Template<T> template, params Type[] types) => new(
            template.Remove(types).Initializers.Append(types.Select(type =>
                new Initializer<T>(type, (in Context _, in T _) => { }))),
            template.Children, template.Size);

        public static Template<T> Add<T, TComponent>(this Template<T> template, TComponent component) => new(
            template.Remove<TComponent>().Initializers.Append(new Initializer<T>(
                typeof(TComponent),
                (in Context context, in T state) => Array.Fill((TComponent[])context.Store, component, context.Index, context.Count))),
            template.Children, template.Size);

        public static Template<T> Add<T, TComponent>(this Template<T> template, Func<T, TComponent> provide) => new(
            template.Remove<TComponent>().Initializers.Append(new Initializer<T>(
                typeof(TComponent),
                (in Context context, in T state) =>
                {
                    var store = (TComponent[])context.Store;
                    for (int i = 0; i < context.Count; i++) store[i + context.Index] = provide(state);
                })),
            template.Children, template.Size);

        public static Template<T> Add<T, TComponent>(this Template<T> template, Func<Entity, T, TComponent> provide) => new(
            template.Remove<TComponent>().Initializers.Append(new Initializer<T>(
                typeof(TComponent),
                (in Context context, in T state) =>
                {
                    var store = (TComponent[])context.Store;
                    var entities = context.Entities;
                    for (int i = 0; i < context.Count; i++) store[i + context.Index] = provide(entities[i], state);
                })),
            template.Children, template.Size);

        public static Template<T> Add<T, TComponent>(this Template<T> template, Func<Entity, Entity, T, TComponent> provide) => new(
            template.Remove<TComponent>().Initializers.Append(new Initializer<T>(
                typeof(TComponent),
                (in Context context, in T state) =>
                {
                    var store = (TComponent[])context.Store;
                    var entities = context.Entities;
                    var parents = context.Parents;
                    for (int i = 0; i < context.Count; i++) store[i + context.Index] = provide(entities[i], parents[i], state);
                })),
            template.Children, template.Size);

        public static Template<TSource> Add<TSource, TTarget>(this Template<TSource> template, Template<TTarget> other, Func<TSource, TTarget> adapt) =>
            template.Add(other.Adapt(adapt));
        public static Template<T> Add<T>(this Template<Unit> template, Template<T> other) => template.Add(new[] { other });
        public static Template<T> Add<T>(this Template<Unit> template, params Template<T>[] others) =>
            All(others.Prepend(template));
        public static Template<T> Add<T>(this Template<T> template, Template<T> other) => template.Add(new[] { other });
        public static Template<T> Add<T>(this Template<T> template, params Template<T>[] others) =>
            All(others.Prepend(template));

        public static Template<T> Remove<T>(this Template<T> template, params Type[] types) =>
            new(template.Initializers.Where(pair => !types.Contains(pair.Type)).ToArray(), template.Children, template.Size);
        public static Template<TSource> Remove<TSource, TTarget>(this Template<TSource> template, params Template<TTarget>[] templates) =>
            template.Remove(templates.Select(template => template.Initializers.Select(pair => pair.Type)).Flatten());
    }
}