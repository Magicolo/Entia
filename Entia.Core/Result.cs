using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Entia.Core
{
    /// <summary>
    /// Interface that allows to interact with an instance of <see cref="Result{T}"/> abstractly.
    /// </summary>
    public interface IResult
    {
        Result.Tags Tag { get; }
        Result<T> Cast<T>();
    }

    /// <summary>
    /// Type that represents the failure to produce a value. Can be implicitly converted to any <see cref="Result{T}"/> type.
    /// </summary>
    public readonly struct Failure : IResult
    {
        public readonly string[] Messages;
        public Failure(params string[] messages) { Messages = messages; }

        Result.Tags IResult.Tag => Result.Tags.Failure;
        Result<T> IResult.Cast<T>() => Result.Failure(Messages);

        public override int GetHashCode() => ArrayUtility.GetHashCode(Messages ?? Array.Empty<string>());
        public override string ToString() => $"{nameof(Result.Tags.Failure)}({string.Join(", ", Messages ?? Array.Empty<string>())})";
    }

    /// <summary>
    /// Monadic structure that represents the possibility of failing to produce a value of type <typeparamref name="T"/>.
    /// In the case of a failure, explanatory messages can be inspected.
    /// </summary>
    public readonly struct Result<T> : IResult, IEquatable<T>
    {
        public static implicit operator Result<T>(T value) => new Result<T>(Result.Tags.Success, value);
        public static implicit operator Result<T>(Failure failure) => new Result<T>(Result.Tags.Failure, default, failure.Messages);
        public static bool operator ==(Result<T> left, T right) => left.TryValue(out var value) && EqualityComparer<T>.Default.Equals(value, right);
        public static bool operator !=(Result<T> left, T right) => !(left == right);
        public static bool operator ==(T left, Result<T> right) => right == left;
        public static bool operator !=(T left, Result<T> right) => !(left == right);

        public Result.Tags Tag { get; }

        readonly T _value;
        readonly string[] _messages;

        Result(Result.Tags tag, T value, params string[] messages)
        {
            Tag = tag;
            _value = value;
            _messages = messages;
        }

        public bool TryValue(out T value)
        {
            value = _value;
            return Tag == Result.Tags.Success;
        }

        public bool TryMessages(out string[] messages)
        {
            messages = _messages;
            return Tag == Result.Tags.Failure;
        }

        public Result<TTo> Cast<TTo>() => this.Bind(value => value is TTo casted ?
            Result.Success(casted) :
            Result.Failure($"Expected value '{value?.ToString() ?? "null"}' to be of type '{typeof(TTo)}'."));

        public bool Equals(T other) => this == other;
        public override bool Equals(object obj) => obj is T value && this == value;

        public override int GetHashCode() => Tag == Result.Tags.Success ?
            EqualityComparer<T>.Default.GetHashCode(_value) :
            Result.Failure(_messages).GetHashCode();

        public override string ToString() => Tag == Result.Tags.Success ?
            $"{nameof(Result.Tags.Success)}({_value})" :
            Result.Failure(_messages).ToString();
    }

    /// <summary>
    /// Module that exposes many common <see cref="Result{T}"/> constructors and utility functions.
    /// </summary>
    public static class Result
    {
        public enum Tags : byte { Failure, Success }

        public static Result<T> Success<T>(T value) => value;
        public static Result<Unit> Success() => Success(default(Unit));
        public static Failure Failure(params string[] messages) => new Failure(messages);
        public static Failure Failure(IEnumerable<string> messages) => Failure(messages.ToArray());
        public static Failure Failure(Exception exception) => Failure(exception.ToString());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Result<T> Try<T>(Func<T> @try, Action @finally = null)
        {
            try { return @try(); }
            catch (Exception exception) { return Failure(exception); }
            finally { @finally?.Invoke(); }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Result<T> Try<TState, T>(TState state, Func<TState, T> @try, Action<TState> @finally = null)
        {
            try { return @try(state); }
            catch (Exception exception) { return Failure(exception); }
            finally { @finally?.Invoke(state); }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Result<Unit> Try(Action @try, Action @finally = null)
        {
            try { @try(); return default(Unit); }
            catch (Exception exception) { return Failure(exception); }
            finally { @finally?.Invoke(); }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Result<Unit> Try<TState>(TState state, Action<TState> @try, Action<TState> @finally = null)
        {
            try { @try(state); return default(Unit); }
            catch (Exception exception) { return Failure(exception); }
            finally { @finally?.Invoke(state); }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Result<TOut> Use<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> use) where TIn : IDisposable
        {
            if (result.TryValue(out var value)) using (value) return Try(value, use);
            return result.Fail();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Result<Unit> Use<T>(this Result<T> result, Action<T> use) where T : IDisposable
        {
            if (result.TryValue(out var value)) using (value) return Try(value, use);
            return result.Ignore();
        }

        public static bool Is<T>(this Result<T> result, Tags tag) => result.Tag == tag;
        public static bool IsSuccess<T>(this Result<T> result) => result.Is(Tags.Success);
        public static bool IsFailure<T>(this Result<T> result) => result.Is(Tags.Failure);
        public static Result<T> AsResult<T>(this T? value) where T : struct =>
            value is T casted ? Success(casted) :
            Failure($"Expected value of type '{typeof(T).FullFormat()}?' to not be 'null'.");
        public static Result<T> AsResult<T>(this Failure failure) => failure;
        public static Result<Unit> AsResult(this Failure failure) => failure;
        public static Result<T> AsResult<T>(this Option<T> option, params string[] messages) =>
            option.TryValue(out var value) ? Success(value) : Failure(messages);
        public static Option<T> AsOption<T>(this Result<T> result) => result.Match(value => Option.From(value), _ => Option.None());
        public static T? AsNullable<T>(this Result<T> result) where T : struct => result.TryValue(out var value) ? (T?)value : null;
        public static Result<T> AsResult<T>(this Or<T, string[]> or) => or.MapRight(messages => Failure(messages)).AsResult();
        public static Result<T> AsResult<T>(this Or<T, string> or) => or.MapRight(message => Failure(message)).AsResult();
        public static Result<T> AsResult<T>(this Or<T, Failure> or) => or.Match(value => Success(value), failure => failure);
        public static Or<T, Failure> AsOr<T>(this Result<T> result) => result.Match(value => Core.Or.Left(value).AsOr<Failure>(), messages => Failure(messages));

        public static string[] Messages<T>(this Result<T> result) => result.TryMessages(out var messages) ? messages : Array.Empty<string>();
        public static Failure Fail<T>(this Result<T> result, params string[] messages) => Failure(result.Messages().Append(messages));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Result<T> Do<T>(this Result<T> result, Action<T> @do)
        {
            if (result.TryValue(out var value)) @do(value);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Result<T> Do<T, TState>(this Result<T> result, TState state, Action<T, TState> @do)
        {
            if (result.TryValue(out var value)) @do(value, state);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Or<T, TState>(this Result<T> result, TState state, Func<string[], TState, T> provide) =>
            result.TryValue(out var current) ? current : provide(result.Messages(), state);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Or<T>(this Result<T> result, Func<string[], T> provide) =>
            result.TryValue(out var current) ? current : provide(result.Messages());

        public static T Or<T>(this Result<T> result, T value) => result.TryValue(out var current) ? current : value;
        public static Result<T> Or<T>(this Result<T> result1, Result<T> result2) => result1.TryValue(out var value1) ? value1 : result2;
        public static Result<T> Or<T>(this Result<T> result1, Result<T> result2, Result<T> result3) => result1.Or(result2).Or(result3);
        public static Result<T> Or<T>(this Result<T> result1, Result<T> result2, Result<T> result3, Result<T> result4) => result1.Or(result2).Or(result3).Or(result4);
        public static Result<T> Or<T>(this Result<T> result1, Result<T> result2, Result<T> result3, Result<T> result4, Result<T> result5) => result1.Or(result2).Or(result3).Or(result4).Or(result5);

        public static T OrThrow<T>(this Result<T> result) => result.Or(messages => throw new InvalidOperationException(string.Join(", ", messages)));
        public static T OrDefault<T>(this Result<T> result) => result.Or(default(T));
        public static Result<Unit> Ignore<T>(this Result<T> result) => result.Map(_ => default(Unit));
        public static Result<object> Box<T>(this Result<T> result) => result.Map(value => (object)value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Result<TOut> Map<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> map)
        {
            if (result.TryValue(out var value)) return map(value);
            return result.Fail();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Result<TOut> Map<TIn, TOut, TState>(this Result<TIn> result, TState state, Func<TIn, TState, TOut> map)
        {
            if (result.TryValue(out var value)) return map(value, state);
            return result.Fail();
        }

        public static Result<T> Filter<T>(this Result<T> result, bool filter, params string[] messages) =>
            filter ? result : result.Fail(messages);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Result<T> Filter<T>(this Result<T> result, Func<T, bool> filter, params string[] messages)
        {
            if (result.TryValue(out var value)) return filter(value) ? result : Failure(messages);
            return result.Fail(messages);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Result<T> Filter<T, TState>(this Result<T> result, TState state, Func<T, TState, bool> filter, params string[] messages)
        {
            if (result.TryValue(out var value)) return filter(value, state) ? result : Failure(messages);
            return result.Fail(messages);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TOut Fold<TIn, TOut>(this Result<TIn> result, TOut seed, Func<TOut, TIn, TOut> fold)
        {
            if (result.TryValue(out var value)) return fold(seed, value);
            return seed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TOut Fold<TIn, TOut, TState>(this Result<TIn> result, TOut seed, TState state, Func<TOut, TIn, TState, TOut> fold)
        {
            if (result.TryValue(out var value)) return fold(seed, value, state);
            return seed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TOut Match<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> success, Func<string[], TOut> failure) =>
            result.TryValue(out var value) ? success(value) : failure(result.Messages());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Result<T> Match<T>(this Result<T> result, Action<T> success, Action<string[]> failure)
        {
            if (result.TryValue(out var value)) success(value);
            else failure(result.Messages());
            return result;
        }

        public static Result<(T1, T2)> And<T1, T2>(this Result<T1> left, T2 right)
        {
            if (left.TryValue(out var value1)) return (value1, right);
            return left.Fail();
        }

        public static Result<(T1, T2)> And<T1, T2>(this Result<T1> left, Result<T2> right)
        {
            if (left.TryValue(out var value1) && right.TryValue(out var value2)) return (value1, value2);
            return Failure(ArrayUtility.Concatenate(left.Messages(), right.Messages()));
        }

        public static Result<(T1, T2, T3)> And<T1, T2, T3>(this Result<(T1, T2)> left, T3 right)
        {
            if (left.TryValue(out var value1)) return (value1.Item1, value1.Item2, right);
            return left.Fail();
        }

        public static Result<(T1, T2, T3)> And<T1, T2, T3>(this Result<(T1, T2)> left, Result<T3> right)
        {
            if (left.TryValue(out var value1) && right.TryValue(out var value2)) return (value1.Item1, value1.Item2, value2);
            return Failure(ArrayUtility.Concatenate(left.Messages(), right.Messages()));
        }

        public static Result<(T1, T2, T3, T4)> And<T1, T2, T3, T4>(this Result<(T1, T2, T3)> left, T4 right)
        {
            if (left.TryValue(out var value1)) return (value1.Item1, value1.Item2, value1.Item3, right);
            return left.Fail();
        }

        public static Result<(T1, T2, T3, T4)> And<T1, T2, T3, T4>(this Result<(T1, T2, T3)> left, Result<T4> right)
        {
            if (left.TryValue(out var value1) && right.TryValue(out var value2)) return (value1.Item1, value1.Item2, value1.Item3, value2);
            return Failure(ArrayUtility.Concatenate(left.Messages(), right.Messages()));
        }

        public static Result<(T1, T2, T3, T4, T5)> And<T1, T2, T3, T4, T5>(this Result<(T1, T2, T3, T4)> left, T5 right)
        {
            if (left.TryValue(out var value1)) return (value1.Item1, value1.Item2, value1.Item3, value1.Item4, right);
            return left.Fail();
        }

        public static Result<(T1, T2, T3, T4, T5)> And<T1, T2, T3, T4, T5>(this Result<(T1, T2, T3, T4)> left, Result<T5> right)
        {
            if (left.TryValue(out var value1) && right.TryValue(out var value2)) return (value1.Item1, value1.Item2, value1.Item3, value1.Item4, value2);
            return Failure(ArrayUtility.Concatenate(left.Messages(), right.Messages()));
        }

        public static Result<(T1, T2, T3)> And<T1, T2, T3>(this Result<T1> result1, Result<T2> result2, Result<T3> result3)
        {
            if (result1.TryValue(out var value1) && result2.TryValue(out var value2) && result3.TryValue(out var value3)) return (value1, value2, value3);
            return Failure(ArrayUtility.Concatenate(result1.Messages(), result2.Messages(), result3.Messages()));
        }

        public static Result<(T1, T2, T3, T4)> And<T1, T2, T3, T4>(this Result<T1> result1, Result<T2> result2, Result<T3> result3, Result<T4> result4)
        {
            if (result1.TryValue(out var value1) && result2.TryValue(out var value2) && result3.TryValue(out var value3) && result4.TryValue(out var value4)) return (value1, value2, value3, value4);
            return Failure(ArrayUtility.Concatenate(result1.Messages(), result2.Messages(), result3.Messages(), result4.Messages()));
        }

        public static Result<(T1, T2, T3, T4, T5)> And<T1, T2, T3, T4, T5>(this Result<T1> result1, Result<T2> result2, Result<T3> result3, Result<T4> result4, Result<T5> result5)
        {
            if (result1.TryValue(out var value1) && result2.TryValue(out var value2) && result3.TryValue(out var value3) && result4.TryValue(out var value4) && result5.TryValue(out var value5)) return (value1, value2, value3, value4, value5);
            return Failure(ArrayUtility.Concatenate(result1.Messages(), result2.Messages(), result3.Messages(), result4.Messages(), result5.Messages()));
        }

        public static Result<TOut> Return<TIn, TOut>(this Result<TIn> result, TOut value)
        {
            if (result.IsSuccess()) return value;
            return result.Fail();
        }

        public static Result<T> Flatten<T>(this Result<Result<T>> result)
        {
            if (result.TryValue(out var value)) return value;
            return result.Fail();
        }

        public static IResult Flatten<T>(this Result<T> result) where T : IResult
        {
            if (result.TryValue(out var value)) return value;
            return result.Fail();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Result<TOut> Bind<TIn, TOut>(this Result<TIn> result, Func<TIn, Result<TOut>> bind)
        {
            if (result.TryValue(out var value)) return bind(value);
            return result.Fail();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Result<TOut> Bind<TIn, TOut, TState>(this Result<TIn> result, TState state, Func<TIn, TState, Result<TOut>> bind)
        {
            if (result.TryValue(out var value)) return bind(value, state);
            return result.Fail();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Result<T> Recover<T>(this Result<T> result, Func<string[], Result<T>> recover) =>
            result.TryMessages(out var messages) ? recover(messages) : result;

        public static Result<T[]> All<T>(this Result<T>[] results)
        {
            if (results.Length == 0) return Success(Array.Empty<T>());
            if (results.Length == 1) return results[0].Map(value => new[] { value });

            var values = new T[results.Length];
            var messages = new List<string>(results.Length);
            var success = true;
            for (var i = 0; i < results.Length; i++)
            {
                var result = results[i];
                if (result.TryValue(out values[i])) continue;

                success = false;
                messages.AddRange(result.Messages());
            }
            return success ? Success(values) : Failure(messages.ToArray());
        }

        public static Result<T[]> All<T>(this IEnumerable<Result<T>> results) => results.ToArray().All();

        public static Result<Unit> All(this Result<Unit>[] results)
        {
            if (results.Length == 0) return Success();
            if (results.Length == 1) return results[0];

            var messages = new List<string>(results.Length);
            var success = true;
            for (var i = 0; i < results.Length; i++)
            {
                var result = results[i];
                if (result.IsSuccess()) continue;

                success = false;
                messages.AddRange(result.Messages());
            }
            return success ? Success() : Failure(messages.ToArray());
        }

        public static Result<Unit> All(this IEnumerable<Result<Unit>> results) => results.ToArray().All();

        public static Result<T> Any<T>(this Result<T>[] results)
        {
            if (results.Length == 0) return Failure();
            if (results.Length == 1) return results[0];

            var messages = new List<string>(results.Length);
            foreach (var result in results)
            {
                if (result.TryValue(out var value)) return value;
                messages.AddRange(result.Messages());
            }
            return Failure(messages.ToArray());
        }

        public static Result<T> Any<T>(this IEnumerable<Result<T>> results) => results.ToArray().Any();
        public static Result<Unit> Any(this Result<Unit>[] results) => results.Any<Unit>().Return(default(Unit));
        public static Result<Unit> Any(this IEnumerable<Result<Unit>> results) => results.ToArray().Any();

        public static IEnumerable<T> Choose<T>(this Result<T>[] results)
        {
            foreach (var result in results) if (result.TryValue(out var value)) yield return value;
        }

        public static IEnumerable<T> Choose<T>(this IEnumerable<Result<T>> results)
        {
            foreach (var result in results) if (result.TryValue(out var value)) yield return value;
        }

        public static Result<T> FirstOrFailure<T>(this IEnumerable<T> source, params string[] messages)
        {
            foreach (var item in source) return item;
            return Failure(messages);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Result<T> FirstOrFailure<T>(this IEnumerable<T> source, Func<T, bool> predicate, params string[] messages)
        {
            foreach (var item in source) if (predicate(item)) return item;
            return Failure(messages);
        }

        public static Result<T> FirstOrFailure<T>(this T[] source, params string[] messages)
        {
            if (source.Length > 0) return source[0];
            return Failure(messages);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Result<T> FirstOrFailure<T>(this T[] source, Func<T, bool> predicate, params string[] messages)
        {
            foreach (var item in source) if (predicate(item)) return item;
            return Failure(messages);
        }

        public static Result<T> LastOrFailure<T>(this IEnumerable<T> source, params string[] messages)
        {
            var result = Failure(messages).AsResult<T>();
            foreach (var item in source) result = item;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Result<T> LastOrFailure<T>(this IEnumerable<T> source, Func<T, bool> predicate, params string[] messages)
        {
            var result = Failure(messages).AsResult<T>();
            foreach (var item in source) if (predicate(item)) result = item;
            return result;
        }

        public static Result<T> LastOrFailure<T>(this T[] source, params string[] messages)
        {
            if (source.Length > 0) return source[source.Length - 1];
            return Failure(messages);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Result<T> LastOrFailure<T>(this T[] source, Func<T, bool> predicate, params string[] messages)
        {
            for (int i = source.Length - 1; i >= 0; i--)
            {
                var item = source[i];
                if (predicate(item)) return item;
            }
            return Failure(messages);
        }

        public static Result<T> Cast<T>(object value) => Success(value).Cast<T>();
        public static Result<TOut> Cast<TIn, TOut>(TIn value) => Success(value).Cast<TOut>();

        public static Result<T> As<T>(this Result<T> result, Type type) =>
            result.Bind(type, (value, state) => As(value, state));

        public static Result<T> As<T>(T value, Type type) =>
            value?.GetType().Is(type) is true ? Success(value) :
            Failure($"Expected value '{value?.ToString() ?? "null"}' to be of type '{type.FullFormat()}'.");
    }
}
