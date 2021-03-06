using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Entia.Core
{
    /// <summary>
    /// Interface that allows to interact with an instance of <see cref="Or{TLeft, TRight}"/> abstractly.
    /// </summary>
    public interface IOr
    {
        Or.Tags Tag { get; }
        object Value { get; }
    }

    public readonly struct Left<T> : IOr
    {
        public static implicit operator Left<T>(T value) => new Left<T>(value);

        public readonly T Value;
        Or.Tags IOr.Tag => Or.Tags.Left;
        object IOr.Value => Value;

        Left(T value) { Value = value; }

        public Or<T, TRight> AsOr<TRight>() => this;
        public override string ToString() => $"{GetType().Format()}({Value})";
        public override int GetHashCode() => EqualityComparer<T>.Default.GetHashCode(Value);
    }

    public readonly struct Right<T> : IOr
    {
        public static implicit operator Right<T>(T value) => new Right<T>(value);

        public readonly T Value;
        Or.Tags IOr.Tag => Or.Tags.Right;
        object IOr.Value => Value;

        Right(T value) { Value = value; }

        public Or<TLeft, T> AsOr<TLeft>() => this;
        public override string ToString() => $"{GetType().Format()}({Value})";
        public override int GetHashCode() => EqualityComparer<T>.Default.GetHashCode(Value);
    }

    public readonly struct Or<TLeft, TRight> : IOr, IEquatable<Or<TLeft, TRight>>, IEquatable<Left<TLeft>>, IEquatable<Right<TRight>>
    {
        public static bool operator ==(Or<TLeft, TRight> or, TLeft left) =>
            or.TryLeft(out var value) && EqualityComparer<TLeft>.Default.Equals(value, left);
        public static bool operator ==(Or<TLeft, TRight> or, TRight right) =>
            or.TryRight(out var value) && EqualityComparer<TRight>.Default.Equals(value, right);
        public static bool operator ==(Or<TLeft, TRight> left, Or<TLeft, TRight> right) =>
            (left.TryLeft(out var leftValue) && right == leftValue) ||
            (left.TryRight(out var rightValue) && right == rightValue);
        public static bool operator ==(TLeft left, Or<TLeft, TRight> or) => or == left;
        public static bool operator ==(TRight right, Or<TLeft, TRight> or) => or == right;
        public static bool operator !=(Or<TLeft, TRight> or, TLeft left) => !(or == left);
        public static bool operator !=(Or<TLeft, TRight> or, TRight right) => !(or == right);
        public static bool operator !=(Or<TLeft, TRight> left, Or<TLeft, TRight> right) => !(left == right);
        public static bool operator !=(TLeft left, Or<TLeft, TRight> or) => !(left == or);
        public static bool operator !=(TRight right, Or<TLeft, TRight> or) => !(right == or);
        public static implicit operator Or<TLeft, TRight>(Left<TLeft> left) => left.Value;
        public static implicit operator Or<TLeft, TRight>(Right<TRight> right) => right.Value;
        public static implicit operator Or<TLeft, TRight>(TLeft value) => new Or<TLeft, TRight>(Or.Tags.Left, value, default);
        public static implicit operator Or<TLeft, TRight>(TRight value) => new Or<TLeft, TRight>(Or.Tags.Right, default, value);
        public static explicit operator TLeft(Or<TLeft, TRight> or) => or.IsLeft() ? or._left : throw new InvalidCastException();
        public static explicit operator TRight(Or<TLeft, TRight> or) => or.IsRight() ? or._right : throw new InvalidCastException();
        public static explicit operator Left<TLeft>(Or<TLeft, TRight> or) => (TLeft)or;
        public static explicit operator Right<TRight>(Or<TLeft, TRight> or) => (TRight)or;

        public Or.Tags Tag { get; }
        public Option<TLeft> Left => this.Match(left => Option.From(left), _ => Option.None());
        public Option<TRight> Right => this.Match(_ => Option.None(), right => Option.From(right));
        object IOr.Value => this.Match(left => (object)left, right => (object)right);

        readonly TLeft _left;
        readonly TRight _right;

        Or(Or.Tags tag, TLeft left, TRight right)
        {
            Tag = tag;
            _left = left;
            _right = right;
        }

        public bool TryLeft(out TLeft value)
        {
            value = _left;
            return Tag == Or.Tags.Left;
        }

        public bool TryRight(out TRight value)
        {
            value = _right;
            return Tag == Or.Tags.Right;
        }

        public override int GetHashCode() => Tag switch
        {
            Or.Tags.Left => Or.Left(_left).GetHashCode(),
            Or.Tags.Right => Or.Right(_right).GetHashCode(),
            _ => 0
        };

        public override string ToString() => Tag switch
        {
            Or.Tags.Left => Or.Left(_left).ToString(),
            Or.Tags.Right => Or.Right(_right).ToString(),
            _ => ""
        };

        public bool Equals(Or<TLeft, TRight> other) => this == other;
        public bool Equals(Left<TLeft> other) => this == other;
        public bool Equals(Right<TRight> other) => this == other;
        public override bool Equals(object obj) =>
            obj is TLeft leftValue ? this == leftValue :
            obj is TRight rightValue ? this == rightValue :
            obj is Left<TLeft> left ? this == left :
            obj is Right<TRight> right && this == right;
    }

    /// <summary>
    /// Module that exposes many common <see cref="Or{TLeft, TRight}"/> constructors and utility functions.
    /// </summary>
    public static class Or
    {
        public enum Tags { None, Left, Right }

        public static Left<T> Left<T>(T value) => value;
        public static Right<T> Right<T>(T value) => value;

        public static bool Is<T>(this T or, Tags tag) where T : IOr => or.Tag == tag;
        public static bool Is<TLeft, TRight>(this Or<TLeft, TRight> or, Tags tag) => or.Tag == tag;
        public static bool IsLeft<T>(this T or) where T : IOr => or.Is(Tags.Left);
        public static bool IsLeft<TLeft, TRight>(this Or<TLeft, TRight> or) => or.Is(Tags.Left);
        public static bool IsRight<T>(this T or) where T : IOr => or.Is(Tags.Right);
        public static bool IsRight<TLeft, TRight>(this Or<TLeft, TRight> or) => or.Is(Tags.Right);

        public static Or<TRight, TLeft> Flip<TLeft, TRight>(this Or<TLeft, TRight> or) =>
            or.Match(left => Right(left).AsOr<TRight>(), right => Left(right));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TOut Match<TLeft, TRight, TOut>(this Or<TLeft, TRight> or, Func<TLeft, TOut> matchLeft, Func<TRight, TOut> matchRight) =>
            or.TryLeft(out var left) ? matchLeft(left) :
            or.TryRight(out var right) ? matchRight(right) :
            throw new InvalidOperationException();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TOut Match<TLeft, TRight, TState, TOut>(this Or<TLeft, TRight> or, TState state, Func<TLeft, TState, TOut> matchLeft, Func<TRight, TState, TOut> matchRight) =>
            or.TryLeft(out var left) ? matchLeft(left, state) :
            or.TryRight(out var right) ? matchRight(right, state) :
            throw new InvalidOperationException();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Or<TTargetLeft, TTargetRight> Map<TSourceLeft, TTargetLeft, TSourceRight, TTargetRight, TState>(this Or<TSourceLeft, TSourceRight> or, TState state, Func<TSourceLeft, TState, TTargetLeft> mapLeft, Func<TSourceRight, TState, TTargetRight> mapRight) =>
            or.TryLeft(out var left) ? Left(mapLeft(left, state)).AsOr<TTargetRight>() :
            or.TryRight(out var right) ? Right(mapRight(right, state)).AsOr<TTargetLeft>() :
            throw new InvalidOperationException();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Or<TTargetLeft, TTargetRight> Map<TSourceLeft, TTargetLeft, TSourceRight, TTargetRight>(this Or<TSourceLeft, TSourceRight> or, Func<TSourceLeft, TTargetLeft> mapLeft, Func<TSourceRight, TTargetRight> mapRight) =>
            or.TryLeft(out var left) ? Left(mapLeft(left)).AsOr<TTargetRight>() :
            or.TryRight(out var right) ? Right(mapRight(right)).AsOr<TTargetLeft>() :
            throw new InvalidOperationException();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Or<TTarget, TRight> MapLeft<TSource, TTarget, TRight, TState>(this Or<TSource, TRight> or, TState state, Func<TSource, TState, TTarget> map) =>
            or.Map(state, map, (value, _) => value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Or<TTarget, TRight> MapLeft<TSource, TTarget, TRight>(this Or<TSource, TRight> or, Func<TSource, TTarget> map) =>
            or.Map(map, value => value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Or<TLeft, TTarget> MapRight<TLeft, TSource, TTarget, TState>(this Or<TLeft, TSource> or, TState state, Func<TSource, TState, TTarget> map) =>
            or.Map(state, (value, _) => value, map);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Or<TLeft, TTarget> MapRight<TLeft, TSource, TTarget>(this Or<TLeft, TSource> or, Func<TSource, TTarget> map) =>
            or.Map(value => value, map);

        public static TLeft LeftOr<TLeft, TRight>(this Or<TLeft, TRight> or, TLeft value) =>
            or.TryLeft(out var left) ? left : value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TLeft LeftOr<TLeft, TRight, TState>(this Or<TLeft, TRight> or, TState state, Func<TRight, TState, TLeft> provide) =>
            or.Match(state, (left, _) => left, provide);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TLeft LeftOr<TLeft, TRight>(this Or<TLeft, TRight> or, Func<TRight, TLeft> provide) =>
            or.Match(left => left, provide);

        public static TRight RightOr<TLeft, TRight>(this Or<TLeft, TRight> or, TRight value) =>
            or.TryRight(out var right) ? right : value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TRight RightOr<TLeft, TRight, TState>(this Or<TLeft, TRight> or, TState state, Func<TLeft, TState, TRight> provide) =>
            or.Match(state, provide, (right, _) => right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TRight RightOr<TLeft, TRight>(this Or<TLeft, TRight> or, Func<TLeft, TRight> provide) =>
            or.Match(provide, right => right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Or<TLeft, TRight> Do<TLeft, TRight, TState>(this Or<TLeft, TRight> or, TState state, Action<TLeft, TState> doLeft, Action<TRight, TState> doRight)
        {
            if (or.TryLeft(out var left)) doLeft(left, state);
            else if (or.TryRight(out var right)) doRight(right, state);
            else throw new InvalidOperationException();
            return or;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Or<TLeft, TRight> Do<TLeft, TRight>(this Or<TLeft, TRight> or, Action<TLeft> doLeft, Action<TRight> doRight)
        {
            if (or.TryLeft(out var left)) doLeft(left);
            else if (or.TryRight(out var right)) doRight(right);
            else throw new InvalidOperationException();
            return or;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Or<TLeft, TRight> DoLeft<TLeft, TRight, TState>(this Or<TLeft, TRight> or, TState state, Action<TLeft, TState> @do) =>
            or.Do(state, @do, (_, __) => { });
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Or<TLeft, TRight> DoLeft<TLeft, TRight>(this Or<TLeft, TRight> or, Action<TLeft> @do) =>
            or.Do(@do, _ => { });
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Or<TLeft, TRight> DoRight<TLeft, TRight, TState>(this Or<TLeft, TRight> or, TState state, Action<TRight, TState> @do) =>
            or.Do(state, (_, __) => { }, @do);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Or<TLeft, TRight> DoRight<TLeft, TRight>(this Or<TLeft, TRight> or, Action<TRight> @do) =>
            or.Do(_ => { }, @do);

        public static Or<Unit, Unit> Ignore<TLeft, TRight>(this Or<TLeft, TRight> or) =>
            or.Map(_ => default(Unit), _ => default(Unit));
        public static Or<Unit, TRight> IgnoreLeft<TLeft, TRight>(this Or<TLeft, TRight> or) =>
            or.MapLeft(_ => default(Unit));
        public static Or<TLeft, Unit> IgnoreRight<TLeft, TRight>(this Or<TLeft, TRight> or) =>
            or.MapRight(_ => default(Unit));

        public static IEnumerable<TLeft> Lefts<TLeft, TRight>(this IEnumerable<Or<TLeft, TRight>> ors)
        {
            foreach (var or in ors) if (or.TryLeft(out var value)) yield return value;
        }

        public static IEnumerable<TRight> Rights<TLeft, TRight>(this IEnumerable<Or<TLeft, TRight>> ors)
        {
            foreach (var or in ors) if (or.TryRight(out var value)) yield return value;
        }
    }
}
