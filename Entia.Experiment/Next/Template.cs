using System;
using System.Collections.Generic;
using System.Linq;
using Entia.Core;
using Entia.Experiment.V4.Nodes;

namespace Entia.Experiment.V4
{
    public readonly ref struct Context
    {
        public readonly int Index;
        public readonly int Count;
        public readonly Segment.Chunk Chunk;

        public Context(int index, int count, Segment.Chunk chunk)
        {
            Index = index;
            Count = count;
            Chunk = chunk;
        }
    }

    public readonly struct Template<T>
    {
        public static readonly Template<T> Empty = new(
            Array.Empty<Template.Initializer<T>>(),
            Array.Empty<Template<T>>(),
            default);

        public static implicit operator Template<T>(Template<Unit> template) => template.Adapt<T>();

        public readonly Template.Initializer<T>[] Initializers;
        public readonly Template<T>[] Children;
        public readonly int? Size;
        public Template(Template.Initializer<T>[] initializers, Template<T>[] children, int? size)
        {
            Initializers = initializers;
            Children = children;
            Size = size;
        }

        public bool Has<TComponent>() => this.Has(typeof(TComponent));
        public Template<T> Add<TComponent>() => this.Add(typeof(TComponent));
        public Template<T> Remove<TComponent>() => this.Remove(typeof(TComponent));
        public Template<TTarget> Adapt<TTarget>(Func<TTarget, T> adapt) => this.Adapt<T, TTarget>(adapt);
        public Template<T> With(Template.Initializer<T>[] initializers = null, Template<T>[] children = null, int? size = null) =>
            new(initializers ?? Initializers, children ?? Children, size ?? Size);
    }

    public static class Template
    {
        public delegate void Initialize<T>(Array store, in Context context, in T state);

        public readonly struct Initializer<T>
        {
            public readonly Type Type;
            public readonly Initialize<T> Initialize;
            public Initializer(Type type, Initialize<T> initialize) { Type = type; Initialize = initialize; }
        }

        public static Template<T> Empty<T>() => Template<T>.Empty;
        public static Template<Unit> Empty() => Empty<Unit>();

        public static Template<T> Size<T>(this Template<T> template, int size) => template.With(size: size);

        public static Template<TTarget> Descend<TSource, TTarget>(this Template<TSource> template, Func<Template<TSource>, Template<TTarget>> map) =>
            map(template).With(children: template.Children.Select(map, Descend));

        public static Template<Unit> Adapt<T>(this Template<T> template, T state) => template.Adapt<T, Unit>(_ => state);
        public static Template<T> Adapt<T>(this Template<Unit> template) => template.Adapt<Unit, T>(_ => default);
        public static Template<TTarget> Adapt<TSource, TTarget>(this Template<TSource> template, Func<TTarget, TSource> adapt) => new(
            template.Initializers.Select(initializer => new Initializer<TTarget>(
                initializer.Type,
                initializer.Initialize == null ? default :
                (Array store, in Context context, in TTarget state) => initializer.Initialize(store, context, adapt(state)))),
            template.Children.Select(child => child.Adapt(adapt)),
            template.Size);

        public static Template<T> Adopt<T>(this Template<T> template, params Template<T>[] children) =>
            template.With(children: template.Children.Append(children));
        public static Template<TSource> Adopt<TSource, TTarget>(this Template<TSource> template, Template<TTarget> child, Func<TSource, TTarget> adapt) =>
            template.Adopt(child.Adapt(adapt));

        public static Template<T> All<T>(params Template<T>[] templates) => templates.All();
        public static Template<T> All<T>(this IEnumerable<Template<T>> templates) => new(
            templates
                .SelectMany(template => template.Initializers)
                .GroupBy(initializer => initializer.Type)
                .Select(group => group.Last())
                .ToArray(),
            templates.SelectMany(template => template.Children).ToArray(),
            templates.Max(template => template.Size));

        public static Template<T> Add<T>(this Template<T> template, Type type) => template.Add(new[] { type });
        public static Template<T> Add<T>(this Template<T> template, params Type[] types) =>
            template.With(template.Remove(types).Initializers.Append(
                types.Select(type => new Initializer<T>(type, default))));

        public static Template<T> Add<T, TComponent>(this Template<T> template, TComponent component) =>
            template.With(template.Remove<TComponent>().Initializers.Append(
                new Initializer<T>(typeof(TComponent), (Array store, in Context context, in T state) =>
                    Array.Fill((TComponent[])store, component, context.Index, context.Count))));

        public static Template<T> Add<T, TComponent>(this Template<T> template, Func<T, TComponent> provide) =>
            template.With(template.Remove<TComponent>().Initializers.Append(
                new Initializer<T>(typeof(TComponent), (Array store, in Context context, in T state) =>
                {
                    var casted = (TComponent[])store;
                    for (int i = 0; i < context.Count; i++) casted[i + context.Index] = provide(state);
                })));

        public static Template<T> Add<T, TComponent>(this Template<T> template, Func<Entity, T, TComponent> provide) =>
            template.With(template.Remove<TComponent>().Initializers.Append(
                new Initializer<T>(typeof(TComponent), (Array store, in Context context, in T state) =>
                {
                    var casted = (TComponent[])store;
                    var entities = context.Chunk.Entities;
                    for (int i = 0; i < context.Count; i++)
                    {
                        var index = context.Index + i;
                        casted[index] = provide(entities[index], state);
                    }
                })));

        public static Template<T> Add<T, TComponent>(this Template<T> template, Func<Entity, Entity, T, TComponent> provide) =>
            template.With(template.Remove<TComponent>().Initializers.Append(
                new Initializer<T>(typeof(TComponent), (Array store, in Context context, in T state) =>
                {
                    var casted = (TComponent[])store;
                    var entities = context.Chunk.Entities;
                    var parents = context.Chunk.Parents;
                    for (int i = 0; i < context.Count; i++)
                    {
                        var index = context.Index + i;
                        casted[index] = provide(entities[index], parents[index], state);
                    }
                })));

        public static Template<T> Add<T, TComponent>(this Template<T> template, Func<Entity, Entity, Slice<Entity>.Read, T, TComponent> provide) =>
            template.With(template.Remove<TComponent>().Initializers.Append(
                new Initializer<T>(typeof(TComponent), (Array store, in Context context, in T state) =>
                {
                    var casted = (TComponent[])store;
                    var entities = context.Chunk.Entities;
                    var parents = context.Chunk.Parents;
                    var children = context.Chunk.Children;
                    for (int i = 0; i < context.Count; i++)
                    {
                        var index = context.Index + i;
                        casted[index] = provide(entities[index], parents[index], children[index].Slice(), state);
                    }
                })));

        public static Template<TSource> Add<TSource, TTarget>(this Template<TSource> template, Template<TTarget> other, Func<TSource, TTarget> adapt) =>
            template.Add(other.Adapt(adapt));
        public static Template<T> Add<T>(this Template<Unit> template, Template<T> other) => template.Add(new[] { other });
        public static Template<T> Add<T>(this Template<Unit> template, params Template<T>[] others) =>
            All(others.Prepend(template));
        public static Template<T> Add<T>(this Template<T> template, Template<T> other) => template.Add(new[] { other });
        public static Template<T> Add<T>(this Template<T> template, params Template<T>[] others) =>
            All(others.Prepend(template));

        public static Template<T> Remove<T>(this Template<T> template, params Type[] types) =>
            template.With(template.Initializers.Where(initializer => !types.Contains(initializer.Type)).ToArray());
        public static Template<TSource> Remove<TSource, TTarget>(this Template<TSource> template, params Template<TTarget>[] templates) =>
            template.Remove(templates.Select(template => template.Initializers.Select(initializer => initializer.Type)).Flatten());

        public static bool Has<T>(this Template<T> template, Type type) =>
            template.Initializers.Any(initializer => initializer.Type == type);
    }
}