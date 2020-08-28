﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Entia.Components;
using Entia.Core;
using Entia.Core.Documentation;

namespace Entia.Modules.Component
{
    [ThreadSafe]
    public static class ComponentUtility
    {
        public enum Kinds { Invalid, Abstract, Concrete }

        [ThreadSafe]
        public static class Concrete<T> where T : struct, IComponent
        {
            [Preserve]
            public static readonly Metadata Data = GetMetadata(typeof(T));
            [Preserve]
            public static readonly Lazy<Metadata> Disabled = new Lazy<Metadata>(() => Concrete<IsDisabled<T>>.Data);
            [Preserve]
            public static readonly Pointer<T> Pointer = new Pointer<T>();
        }

        [ThreadSafe]
        public static class Abstract<T> where T : IComponent
        {
            public static readonly Kinds Kind = GetKind(typeof(T));
            public static readonly Metadata Data = GetMetadata(typeof(T));

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool TryConcrete(out Metadata data)
            {
                data = Data;
                return Kind == Kinds.Concrete;
            }
        }

        struct State
        {
            public (Metadata[] items, int count) Concretes;
            public TypeMap<IComponent, Metadata> ConcreteToMetadata;
            public TypeMap<IComponent, BitMask> AbstractToMask;
            public TypeMap<IComponent, Metadata[]> AbstractToMetadata;
        }

        public static int Count => _state.Read((in State state) => state.Concretes.count);

        static readonly ConcurrentDictionary<BitMask, Metadata[]> _maskToMetadata = new ConcurrentDictionary<BitMask, Metadata[]>();
        static readonly Concurrent<State> _state = new State
        {
            Concretes = (new Metadata[8], 0),
            ConcreteToMetadata = new TypeMap<IComponent, Metadata>(),
            AbstractToMask = new TypeMap<IComponent, BitMask>(),
            AbstractToMetadata = new TypeMap<IComponent, Metadata[]>(),
        };

        public static bool TryGetMetadata(Type type, bool create, out Metadata data)
        {
            using var read = _state.Read(create);
            if (read.Value.ConcreteToMetadata.TryGet(type, out data)) return data.IsValid;
            else if (create) data = CreateMetadata(type);
            return data.IsValid;
        }

        public static bool TryGetConcreteMask<T>(out BitMask mask) where T : IComponent
        {
            using var read = _state.Read();
            return read.Value.AbstractToMask.TryGet<T>(out mask);
        }

        public static bool TryGetConcreteTypes<T>(out Metadata[] types) where T : IComponent
        {
            using var read = _state.Read();
            return read.Value.AbstractToMetadata.TryGet<T>(out types);
        }

        public static bool TryGetConcrete<T>(out BitMask mask, out Metadata[] types) where T : IComponent
        {
            using var read = _state.Read();
            return read.Value.AbstractToMask.TryGet<T>(out mask) & read.Value.AbstractToMetadata.TryGet<T>(out types);
        }

        public static bool TryGetConcreteMask(Type type, out BitMask mask)
        {
            using var read = _state.Read();
            return read.Value.AbstractToMask.TryGet(type, out mask);
        }

        public static bool TryGetConcreteTypes(Type type, out Metadata[] types)
        {
            using var read = _state.Read();
            return read.Value.AbstractToMetadata.TryGet(type, out types);
        }

        public static bool TryGetConcrete(Type type, out BitMask mask, out Metadata[] types)
        {
            using var read = _state.Read();
            return read.Value.AbstractToMask.TryGet(type, out mask) & read.Value.AbstractToMetadata.TryGet(type, out types);
        }

        public static Metadata[] GetConcreteTypes(BitMask mask) => _maskToMetadata.GetOrAdd(mask, key => CreateConcreteTypes(key));

        public static Metadata GetMetadata(Type type)
        {
            using var read = _state.Read(true);
            if (read.Value.ConcreteToMetadata.TryGet(type, out var data)) return data;
            return CreateMetadata(type);
        }

        public static bool TryGetMetadata(int index, out Metadata data)
        {
            using var read = _state.Read();
            return read.Value.Concretes.TryGet(index, out data) && data.IsValid;
        }

        public static BitMask GetConcreteMask(Type type)
        {
            using var read = _state.Read(true);
            if (read.Value.AbstractToMask.TryGet(type, out var mask)) return mask;
            using var write = _state.Write();
            return
                write.Value.AbstractToMask.TryGet(type, out mask) ? mask :
                write.Value.AbstractToMask[type] = new BitMask();
        }

        public static Metadata[] ToMetadata(BitMask mask)
        {
            using var read = _state.Read();
            return mask
                .Select(index => index < read.Value.Concretes.count ? read.Value.Concretes.items[index] : default)
                .Where(data => data.IsValid)
                .ToArray();
        }

        public static Kinds GetKind(Type type)
        {
            if (type.Is<IComponent>())
                return type.IsValueType && type.IsSealed && !type.IsGenericTypeDefinition && !type.IsAbstract ? Kinds.Concrete : Kinds.Abstract;
            return Kinds.Invalid;
        }

        public static bool IsInvalid(Type type) => GetKind(type) == Kinds.Invalid;
        public static bool IsValid(Type type) => !IsInvalid(type);
        public static bool IsConcrete(Type type) => GetKind(type) == Kinds.Concrete;
        public static bool IsAbstract(Type type) => GetKind(type) == Kinds.Abstract;

        public static BitMask[] GetMasks(params Type[] types)
        {
            var (concrete, @abstract) = types.Where(IsValid).Split(IsConcrete);
            var masks = @abstract.Select(type => GetConcreteMask(type));
            if (concrete.Length > 0) masks = masks.Prepend(new BitMask(concrete.Select(component => GetMetadata(component).Index)));
            return masks.ToArray();
        }

        static Metadata CreateMetadata(Type type)
        {
            if (IsConcrete(type))
            {
                var abstracts = type.Hierarchy()
                    .SelectMany(child => child.IsGenericType ? new[] { child, child.GetGenericTypeDefinition() } : new[] { child })
                    .Where(child => child.Is<IComponent>())
                    .ToArray();

                using var write = _state.Write();
                if (write.Value.ConcreteToMetadata.TryGet(type, out var data)) return data;

                var index = write.Value.Concretes.count;
                data = new Metadata(ReflectionUtility.GetData(type), index);
                write.Value.ConcreteToMetadata[type] = data;
                write.Value.Concretes.Push(data);
                foreach (var @abstract in abstracts)
                {
                    if (write.Value.AbstractToMask.TryGet(@abstract, out var mask)) mask.Add(index);
                    else write.Value.AbstractToMask[@abstract] = new BitMask(index);

                    ref var types = ref write.Value.AbstractToMetadata.Get(@abstract, out var success);
                    if (success) ArrayUtility.Append(ref types, data);
                    else write.Value.AbstractToMetadata[@abstract] = new[] { data };
                }
                return data;
            }

            return default;
        }

        static Metadata[] CreateConcreteTypes(BitMask mask)
        {
            var list = new List<Metadata>(mask.Capacity);
            foreach (var index in mask) if (TryGetMetadata(index, out var metadata)) list.Add(metadata);
            return list.ToArray();
        }
    }
}