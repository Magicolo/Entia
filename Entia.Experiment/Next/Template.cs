using System;
using System.Collections.Generic;
using System.Linq;
using Entia.Core;

namespace Entia.Experiment.V4
{
    public readonly struct Template<T>
    {
        public delegate void Initialize(int index, int count, Array store, in T state);

        public readonly (Type type, Initialize initialize)[] Initializers;
        public Template(params (Type type, Initialize initialize)[] initializers) => Initializers = initializers;
        public Template<T> Add<TComponent>() => this.Add(default(TComponent));
        public Template<T> Remove<TComponent>() => this.Remove(typeof(TComponent));
    }

    public static class Template
    {
        public static Template<Unit> Create() => Create<Unit>();
        public static Template<TState> Create<TState>() => new Template<TState>(Array.Empty<(Type, Template<TState>.Initialize)>());

        public static Template<TTarget> Adapt<TSource, TTarget>(this Template<TSource> template, Func<TTarget, TSource> adapt) =>
            new(template.Initializers.Select(pair => (pair.type,
                new Template<TTarget>.Initialize((int index, int count, Array store, in TTarget state) =>
                    pair.initialize(index, count, store, adapt(state))))));

        public static Template<TState> All<TState>(params Template<TState>[] templates) => templates.All();
        public static Template<TState> All<TState>(this IEnumerable<Template<TState>> templates) => new(templates
            .SelectMany(template => template.Initializers)
            .GroupBy(pair => pair.type)
            .Select(group => group.Last())
            .ToArray());

        public static Template<TState> Add<TState, TComponent>(this Template<TState> template, TComponent component) =>
            new(template.Remove<TComponent>().Initializers.Append((typeof(TComponent),
                new((int index, int count, Array store, in TState state) =>
                {
                    var casted = (TComponent[])store;
                    for (int i = 0; i < count; i++) casted[i + index] = component;
                }))));

        public static Template<TState> Add<TState, TComponent>(this Template<TState> template, Func<TState, TComponent> provide) =>
            new(template.Remove<TComponent>().Initializers.Append((typeof(TComponent),
                new((int index, int count, Array store, in TState state) =>
                {
                    var casted = (TComponent[])store;
                    for (int i = 0; i < count; i++) casted[i + index] = provide(state);
                }))));

        public static Template<TState> Remove<TState>(this Template<TState> template, params Type[] types) =>
            new(template.Initializers.Where(pair => !types.Contains(pair.type)).ToArray());
    }
}