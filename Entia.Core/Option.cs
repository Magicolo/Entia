using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Entia.Core
{
    /// <summary>
    /// Interface that allows to interact with an instance of <see cref="Option{T}"/> abstractly.
    /// </summary>
    public interface IOption
    {
        Option.Tags Tag { get; }
        object Value { get; }
        Option<T> Cast<T>();
    }

    /// <summary>
    /// Unit type that represents the absence of a value. Can be implicitly converted to any <see cref="Option{T}"/> type.
    /// </summary>
    public readonly struct None : IOption
    {
        Option.Tags IOption.Tag => Option.Tags.None;
        object IOption.Value => null;
        Option<T> IOption.Cast<T>() => this;
        public override int GetHashCode() => 0;
        public override string ToString() => nameof(Option.Tags.None);
    }

    /// <summary>
    /// Monadic structure that represents the possibility of an absent value of type <typeparamref name="T"/>.
    /// This differs from the <see cref="Nullable{T}"/> type since it covers all type <typeparamref name="T"/>, not just
    /// value types.
    /// </summary>
    public readonly struct Option<T> : IOption, IEquatable<Option<T>>, IEquatable<T>
    {
        public static implicit operator Option<T>(T value) => new Option<T>(value == null ? Option.Tags.None : Option.Tags.Some, value);
        public static implicit operator Option<T>(None _) => default;
        public static bool operator ==(Option<T> left, T right) => left.TryValue(out var value) && EqualityComparer<T>.Default.Equals(value, right);
        public static bool operator !=(Option<T> left, T right) => !(left == right);
        public static bool operator ==(T left, Option<T> right) => right == left;
        public static bool operator !=(T left, Option<T> right) => !(left == right);
        public static bool operator ==(Option<T> left, Option<T> right) => left.TryValue(out var value) ? right == value : right.IsNone();
        public static bool operator !=(Option<T> left, Option<T> right) => !(left == right);

        public Option.Tags Tag { get; }
        object IOption.Value => this.Match(value => (object)value, () => null);

        readonly T _value;

        Option(Option.Tags tag, T value)
        {
            Tag = tag;
            _value = value;
        }

        public bool TryValue(out T value)
        {
            value = _value;
            return Tag == Option.Tags.Some;
        }

        public Option<TTo> Cast<TTo>() => this.Bind(value => value is TTo casted ? Option.From(casted) : Option.None());
        public bool Equals(Option<T> other) => this == other;
        public bool Equals(T other) => this == other;
        public override bool Equals(object obj) =>
            obj is T value ? this == value :
            obj is Option<T> option ? this == option :
            obj is null || obj is None == this.IsNone();

        public override int GetHashCode() => Tag == Option.Tags.Some ? _value.GetHashCode() : Option.None().GetHashCode();
        public override string ToString() => Tag == Option.Tags.Some ? $"{nameof(Option.Tags.Some)}({_value})" : Option.None().ToString();
    }

    /// <summary>
    /// Module that exposes many common <see cref="Option{T}"/> constructors and utility functions.
    /// </summary>
    public static class Option
    {
        public enum Tags : byte { None, Some }

        public static Option<T> Some<T>(T value) where T : struct => value;
        public static Option<Unit> Some() => Some(default(Unit));
        public static None None() => new None();
        public static Option<T> From<T>(T value) => value;
        public static Option<T> From<T>(T? value) where T : struct => value.HasValue ? From(value.Value) : None();

        public static bool Is<T>(this T option, Tags tag) where T : IOption => option.Tag == tag;
        public static bool Is<T>(this Option<T> option, Tags tag) => option.Tag == tag;
        public static bool IsSome<T>(this T option) where T : IOption => option.Is(Tags.Some);
        public static bool IsSome<T>(this Option<T> option) => option.Is(Tags.Some);
        public static bool IsNone<T>(this T option) where T : IOption => option.Is(Tags.None);
        public static bool IsNone<T>(this Option<T> option) => option.Is(Tags.None);
        public static Option<T> AsOption<T>(this T? value) where T : struct => From(value);
        public static Option<T> AsOption<T>(this None none) => none;
        public static T? AsNullable<T>(this Option<T> option) where T : struct => option.TryValue(out var value) ? (T?)value : null;
        public static Or<T, None> AsOr<T>(this Option<T> option) => option.Match(value => Core.Or.Left(value).AsOr<None>(), () => None());
        public static Option<T> AsOption<T>(this Or<T, Unit> or) => or.MapRight(_ => None()).AsOption();
        public static Option<T> AsOption<T>(this Or<T, None> or) => or.Match(value => From(value), none => none);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Option<T> Try<T>(Func<T> @try, Action @finally = null)
        {
            try { return @try(); }
            catch { return None(); }
            finally { @finally?.Invoke(); }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Option<T> Try<TState, T>(TState state, Func<TState, T> @try, Action<TState> @finally = null)
        {
            try { return @try(state); }
            catch { return None(); }
            finally { @finally?.Invoke(state); }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Option<Unit> Try(Action @try, Action @finally = null)
        {
            try { @try(); return default(Unit); }
            catch { return None(); }
            finally { @finally?.Invoke(); }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Option<Unit> Try<TState>(TState state, Action<TState> @try, Action<TState> @finally = null)
        {
            try { @try(state); return default(Unit); }
            catch { return None(); }
            finally { @finally?.Invoke(state); }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Option<T> Do<T>(this Option<T> option, Action<T> @do)
        {
            if (option.TryValue(out var value)) @do(value);
            return option;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Option<T> Do<T, TState>(this Option<T> option, TState state, Action<T, TState> @do)
        {
            if (option.TryValue(out var value)) @do(value, state);
            return option;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Or<T, TState>(this Option<T> option, TState state, Func<TState, T> provide) =>
            option.TryValue(out var current) ? current : provide(state);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Or<T>(this Option<T> option, Func<T> provide) =>
            option.TryValue(out var current) ? current : provide();

        public static T Or<T>(this Option<T> option, T @default) => option.TryValue(out var value) ? value : @default;
        public static Option<T> Or<T>(this Option<T> option1, Option<T> option2) => option1.TryValue(out var value1) ? value1 : option2;
        public static Option<T> Or<T>(this Option<T> option1, Option<T> option2, Option<T> option3) => option1.Or(option2).Or(option3);
        public static Option<T> Or<T>(this Option<T> option1, Option<T> option2, Option<T> option3, Option<T> option4) => option1.Or(option2).Or(option3).Or(option4);
        public static Option<T> Or<T>(this Option<T> option1, Option<T> option2, Option<T> option3, Option<T> option4, Option<T> option5) => option1.Or(option2).Or(option3).Or(option4).Or(option5);
        public static T OrThrow<T>(this Option<T> option, string message) => option.Or(message, state => throw new InvalidOperationException(state));
        public static T OrThrow<T>(this Option<T> option) => option.Or(() => throw new InvalidOperationException());
        public static T OrDefault<T>(this Option<T> option) => option.Or(default(T));
        public static Option<Unit> Ignore<T>(this Option<T> option) => option.Map(_ => default(Unit));
        public static Option<object> Box<T>(this Option<T> option) => option.Map(value => (object)value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Option<TOut> Map<TIn, TOut>(this Option<TIn> option, Func<TIn, TOut> map)
        {
            if (option.TryValue(out var value)) return map(value);
            return None();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Option<TOut> Map<TIn, TOut, TState>(this Option<TIn> option, TState state, Func<TIn, TState, TOut> map)
        {
            if (option.TryValue(out var value)) return map(value, state);
            return None();
        }

        public static Option<T> Filter<T>(this Option<T> option, bool filter) => filter ? option : None();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Option<T> Filter<T>(this Option<T> option, Func<T, bool> filter)
        {
            if (option.TryValue(out var value)) return filter(value) ? option : None();
            return None();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Option<T> Filter<T, TState>(this Option<T> option, TState state, Func<T, TState, bool> filter)
        {
            if (option.TryValue(out var value)) return filter(value, state) ? option : None();
            return None();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TOut Fold<TIn, TOut>(this Option<TIn> option, TOut seed, Func<TOut, TIn, TOut> fold)
        {
            if (option.TryValue(out var value)) return fold(seed, value);
            return seed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TOut Fold<TIn, TOut, TState>(this Option<TIn> option, TOut seed, TState state, Func<TOut, TIn, TState, TOut> fold)
        {
            if (option.TryValue(out var value)) return fold(seed, value, state);
            return seed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TOut Match<TIn, TOut>(this Option<TIn> option, Func<TIn, TOut> some, Func<TOut> none) =>
            option.TryValue(out var value) ? some(value) : none();
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TOut Match<TIn, TOut, TState>(this Option<TIn> option, TState state, Func<TIn, TState, TOut> some, Func<TState, TOut> none) =>
            option.TryValue(out var value) ? some(value, state) : none(state);

        public static Option<(T1, T2)> And<T1, T2>(this Option<T1> left, T2 right)
        {
            if (left.TryValue(out var value1)) return (value1, right);
            return None();
        }

        public static Option<(T1, T2)> And<T1, T2>(this Option<T1> left, Option<T2> right)
        {
            if (left.TryValue(out var value1) && right.TryValue(out var value2)) return (value1, value2);
            return None();
        }

        public static Option<(T1, T2, T3)> And<T1, T2, T3>(this Option<(T1, T2)> left, T3 right)
        {
            if (left.TryValue(out var value1)) return (value1.Item1, value1.Item2, right);
            return None();
        }

        public static Option<(T1, T2, T3)> And<T1, T2, T3>(this Option<(T1, T2)> left, Option<T3> right)
        {
            if (left.TryValue(out var value1) && right.TryValue(out var value2)) return (value1.Item1, value1.Item2, value2);
            return None();
        }

        public static Option<(T1, T2, T3, T4)> And<T1, T2, T3, T4>(this Option<(T1, T2, T3)> left, T4 right)
        {
            if (left.TryValue(out var value1)) return (value1.Item1, value1.Item2, value1.Item3, right);
            return None();
        }

        public static Option<(T1, T2, T3, T4)> And<T1, T2, T3, T4>(this Option<(T1, T2, T3)> left, Option<T4> right)
        {
            if (left.TryValue(out var value1) && right.TryValue(out var value2)) return (value1.Item1, value1.Item2, value1.Item3, value2);
            return None();
        }

        public static Option<(T1, T2, T3, T4, T5)> And<T1, T2, T3, T4, T5>(this Option<(T1, T2, T3, T4)> left, T5 right)
        {
            if (left.TryValue(out var value1)) return (value1.Item1, value1.Item2, value1.Item3, value1.Item4, right);
            return None();
        }

        public static Option<(T1, T2, T3, T4, T5)> And<T1, T2, T3, T4, T5>(this Option<(T1, T2, T3, T4)> left, Option<T5> right)
        {
            if (left.TryValue(out var value1) && right.TryValue(out var value2)) return (value1.Item1, value1.Item2, value1.Item3, value1.Item4, value2);
            return None();
        }

        public static Option<(T1, T2, T3)> And<T1, T2, T3>(this Option<T1> option1, Option<T2> option2, Option<T3> option3)
        {
            if (option1.TryValue(out var value1) && option2.TryValue(out var value2) && option3.TryValue(out var value3)) return (value1, value2, value3);
            return None();
        }

        public static Option<(T1, T2, T3, T4)> And<T1, T2, T3, T4>(this Option<T1> option1, Option<T2> option2, Option<T3> option3, Option<T4> option4)
        {
            if (option1.TryValue(out var value1) && option2.TryValue(out var value2) && option3.TryValue(out var value3) && option4.TryValue(out var value4)) return (value1, value2, value3, value4);
            return None();
        }

        public static Option<(T1, T2, T3, T4, T5)> And<T1, T2, T3, T4, T5>(this Option<T1> option1, Option<T2> option2, Option<T3> option3, Option<T4> option4, Option<T5> option5)
        {
            if (option1.TryValue(out var value1) && option2.TryValue(out var value2) && option3.TryValue(out var value3) && option4.TryValue(out var value4) && option5.TryValue(out var value5)) return (value1, value2, value3, value4, value5);
            return None();
        }

        public static Option<T1> Left<T1, T2>(this Option<(T1, T2)> option) => option.Map(pair => pair.Item1);
        public static Option<T2> Right<T1, T2>(this Option<(T1, T2)> option) => option.Map(pair => pair.Item2);

        public static Option<TOut> Return<TIn, TOut>(this Option<TIn> option, TOut value)
        {
            if (option.IsSome()) return value;
            return None();
        }

        public static Option<T> Flatten<T>(this Option<Option<T>> option)
        {
            if (option.TryValue(out var value)) return value;
            return None();
        }

        public static IOption Flatten<T>(this Option<T> option) where T : IOption
        {
            if (option.TryValue(out var value)) return value;
            return None();
        }

        public static Option<T> Flatten<T>(this Option<T>? option)
        {
            if (option.HasValue) return option.Value;
            return None();
        }

        public static Option<T> Flatten<T>(this Option<T?> option) where T : struct
        {
            if (option.TryValue(out var value)) return value.AsOption();
            return None();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Option<TOut> Bind<TIn, TOut>(this Option<TIn> option, Func<TIn, Option<TOut>> bind)
        {
            if (option.TryValue(out var value)) return bind(value);
            return None();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Option<TOut> Bind<TIn, TOut, TState>(this Option<TIn> option, TState state, Func<TIn, TState, Option<TOut>> bind)
        {
            if (option.TryValue(out var value)) return bind(value, state);
            return None();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Option<T> Recover<T>(this Option<T> option, Func<Option<T>> recover) => option.IsNone() ? recover() : option;

        public static bool TryTake<T>(ref this Option<T> option, out T value) => option.Take().TryValue(out value);

        public static Option<T> Take<T>(ref this Option<T> option)
        {
            var copy = option;
            option = None();
            return copy;
        }

        public static Option<T[]> All<T>(this Option<T>[] options)
        {
            if (options.Length == 0) return From(Array.Empty<T>());

            var values = new T[options.Length];
            for (var i = 0; i < options.Length; i++)
            {
                if (options[i].TryValue(out values[i])) continue;
                else return None();
            }
            return values;
        }

        public static Option<T[]> All<T>(this IEnumerable<Option<T>> options) => All(options.ToArray());

        public static Option<T> Any<T>(this Option<T>[] options)
        {
            foreach (var option in options) if (option.TryValue(out var value)) return value;
            return None();
        }

        public static Option<T> Any<T>(this IEnumerable<Option<T>> options)
        {
            foreach (var option in options) if (option.TryValue(out var value)) return value;
            return None();
        }

        public static IEnumerable<T> Choose<T>(this Option<T>[] options)
        {
            foreach (var option in options) if (option.TryValue(out var value)) yield return value;
        }

        public static IEnumerable<T> Choose<T>(this IEnumerable<Option<T>> options)
        {
            foreach (var option in options) if (option.TryValue(out var value)) yield return value;
        }

        public static IEnumerable<TResult> Choose<TSource, TResult>(this TSource[] source, Func<TSource, Option<TResult>> map)
        {
            foreach (var item in source) if (map(item).TryValue(out var value)) yield return value;
        }

        public static IEnumerable<TResult> Choose<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, Option<TResult>> map)
        {
            foreach (var item in source) if (map(item).TryValue(out var value)) yield return value;
        }

        public static Option<T> FirstOrNone<T>(this IEnumerable<T> source)
        {
            foreach (var item in source) return item;
            return None();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Option<T> FirstOrNone<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            foreach (var item in source) if (predicate(item)) return item;
            return None();
        }

        public static Option<T> FirstOrNone<T>(this T[] source)
        {
            if (source.Length > 0) return source[0];
            return None();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Option<T> FirstOrNone<T>(this T[] source, Func<T, bool> predicate)
        {
            foreach (var item in source) if (predicate(item)) return item;
            return None();
        }

        public static Option<T> LastOrNone<T>(this IEnumerable<T> source)
        {
            var option = None().AsOption<T>();
            foreach (var item in source) option = item;
            return option;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Option<T> LastOrNone<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            var option = None().AsOption<T>();
            foreach (var item in source) if (predicate(item)) option = item;
            return option;
        }

        public static Option<T> LastOrNone<T>(this T[] source)
        {
            if (source.Length > 0) return source[source.Length - 1];
            return None();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Option<T> LastOrNone<T>(this T[] source, Func<T, bool> predicate)
        {
            for (int i = source.Length - 1; i >= 0; i--)
            {
                var item = source[i];
                if (predicate(item)) return item;
            }
            return None();
        }

        public static Option<T> Cast<T>(object value) => From(value).Cast<T>();
        public static Option<TOut> Cast<TIn, TOut>(TIn value) => From(value).Cast<TOut>();
    }
}
