using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Entia.Core;

namespace Entia.Json
{
    /// <summary>
    /// Data structure that represents a json parse tree.
    /// <para>
    /// Many constructors, operators and extensions are provided for this type to make it easy
    /// to manipulate it.
    /// </para>
    /// </summary>
    public sealed class Node : IEquatable<Node>
    {
        // These enums are kept as 'byte' to keep the size of nodes small.
        // Current size is 22 bytes on x64 (24 with padding) and should not go over 24 bytes since
        // it will significantly increase the size of parse trees.
        public enum Kinds : byte
        {
            Null,
            Boolean,
            Number,
            String,
            Object,
            Array,
            Type,
            Reference,
            Abstract
        }

        public enum Tags : byte
        {
            None,
            Plain = 1 << 0,
            Special = 1 << 1,
        }

        public static implicit operator Node(bool value) => Boolean(value);
        public static implicit operator Node(byte value) => Number(value);
        public static implicit operator Node(sbyte value) => Number(value);
        public static implicit operator Node(short value) => Number(value);
        public static implicit operator Node(ushort value) => Number(value);
        public static implicit operator Node(int value) => Number(value);
        public static implicit operator Node(uint value) => Number(value);
        public static implicit operator Node(long value) => Number(value);
        public static implicit operator Node(ulong value) => Number(value);
        public static implicit operator Node(float value) => Number(value);
        public static implicit operator Node(double value) => Number(value);
        public static implicit operator Node(decimal value) => Number(value);
        public static implicit operator Node(Enum value) => Number(value);
        public static implicit operator Node(char value) => Number(value);
        public static implicit operator Node(string value) => String(value);
        public static implicit operator Node(Type value) => Type(value);

        public static bool operator ==(Node left, Node right) => left is null ? right is null : left.Equals(right);
        public static bool operator !=(Node left, Node right) => !(left == right);

        const int Numbers = 128;
        const int Characters = 128;

        // Must be placed before calling the 'Node' constructors.
        static readonly Node[] _empty = { };
        static readonly Node[] _positives = new Node[Numbers];
        static readonly Node[] _negatives = new Node[Numbers];
        static readonly Node[] _characters = new Node[Characters];
        static readonly Node[] _dollars = new Node[Characters];
        // It has been estimated to be very improbable that this counter would overflow and cause
        // identifier collisions within the same node tree.
        static int _counter;

        public static readonly Node Null = new Node(Kinds.Null, Tags.None, null, _empty);
        public static readonly Node True = new Node(Kinds.Boolean, Tags.None, true, _empty);
        public static readonly Node False = new Node(Kinds.Boolean, Tags.None, false, _empty);
        public static readonly Node Zero = new Node(Kinds.Number, Tags.None, 0m, _empty);
        public static readonly Node EmptyObject = new Node(Kinds.Object, Tags.None, null, _empty);
        public static readonly Node EmptyArray = new Node(Kinds.Array, Tags.None, null, _empty);
        public static readonly Node EmptyString = new Node(Kinds.String, Tags.Plain, "", _empty);

        internal static readonly Node DollarT = Dollar('t');
        internal static readonly Node DollarI = Dollar('i');
        internal static readonly Node DollarV = Dollar('v');
        internal static readonly Node DollarR = Dollar('r');
        internal static readonly Node DollarK = Dollar('k');
        internal static readonly Node DollarA = Dollar('a');

        public static Node Boolean(bool value) => value ? True : False;
        public static Node Number(char value) => Number((decimal)value);
        public static Node Number(byte value) => Number((decimal)value);
        public static Node Number(sbyte value) => Number((decimal)value);
        public static Node Number(short value) => Number((decimal)value);
        public static Node Number(ushort value) => Number((decimal)value);
        public static Node Number(int value) => Number((decimal)value);
        public static Node Number(uint value) => Number((decimal)value);
        public static Node Number(long value) => Number((decimal)value);
        public static Node Number(ulong value) => Number((decimal)value);
        public static Node Number(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) ? Null :
            value <= (float)decimal.MinValue || value >= (float)decimal.MaxValue ? Zero :
            Number((decimal)value);
        public static Node Number(double value) =>
            double.IsNaN(value) || double.IsInfinity(value) ? Null :
            value <= (double)decimal.MinValue || value >= (double)decimal.MaxValue ? Zero :
            Number((decimal)value);
        public static Node Number(decimal value)
        {
            if (value == 0) return Zero;
            if (value <= -Numbers || value >= Numbers) return new Node(Kinds.Number, Tags.None, value, _empty);

            var integer = (int)value;
            if (value == integer)
            {
                ref var number = ref integer > 0 ? ref _positives[integer] : ref _negatives[-integer];
                return number ?? (number = new Node(Kinds.Number, Tags.None, value, _empty));
            }
            return new Node(Kinds.Number, Tags.None, value, _empty);
        }
        public static Node Number(Enum value) => value == null ? Null : Number(Convert.ToInt64(value, CultureInfo.InvariantCulture));
        public static Node String(char value)
        {
            if (value >= Characters) return String(value.ToString(), DefaultTags(value));
            return _characters[value] ?? (_characters[value] = String(value.ToString(), GetTags(value)));
        }

        public static Node String(Enum value) => value == null ? Null : String(value.ToString(), Tags.Plain);
        public static Node String(string value)
        {
            if (value == null) return Null;
            if (value.Length == 0) return EmptyString;
            if (value.Length == 1) return String(value[0]);
            if (value.Length == 2 && value[0] == '$') return Dollar(value[1]);
            return String(value, Tags.None);
        }

        public static Node Array(params Node[] items) => Array(items, Tags.None);
        public static Node Object(params (Node key, Node value)[] members) => Object(members.Flatten());
        public static Node Object(params Node[] members) => Object(members, Tags.None);
        public static Node Type(Type type) => type == null ? Null : new Node(Kinds.Type, Tags.None, type, _empty);
        public static Node Abstract(Node type, Node value) => new Node(Kinds.Abstract, Tags.None, null, type, value);
        public static Node Reference(uint identifier) => new Node(Kinds.Reference, Tags.None, identifier, _empty);

        internal static Node Array(Node[] items, Tags tags) => items.Length == 0 ? EmptyArray : new Node(Kinds.Array, tags, null, items);
        internal static Node Object(Node[] members, Tags tags) => members.Length == 0 ? EmptyObject : new Node(Kinds.Object, tags, null, members);
        internal static uint Reserve() => (uint)Interlocked.Increment(ref _counter);
        internal static Node String(string value, Tags tags) => new Node(Kinds.String, tags, value, _empty);
        internal static Node Dollar(char value)
        {
            if (value >= Characters) return String("$" + value, Tags.Special | DefaultTags(value));
            return _dollars[value] ?? (_dollars[value] = String("$" + value, GetTags(value) | Tags.Special));
        }

        static Tags GetTags(char value)
        {
            switch (value)
            {
                case '\n':
                case '\b':
                case '\f':
                case '\r':
                case '\t':
                case '"':
                case '\\': return Tags.None;
                case '$': return Tags.Plain;
                default: return DefaultTags(value);
            }
        }

        static Tags DefaultTags(char value) => value <= byte.MaxValue ? Tags.Plain : Tags.None;

        public Node this[string key] => this.TryMember(key, out var value) ? value : throw new ArgumentException(nameof(key));
        public Node this[int index] => this.TryItem(index, out var item) ? item : throw new ArgumentException(nameof(index));

        public readonly uint Identifier;
        public readonly Kinds Kind;
        public readonly Tags Tag;
        public readonly object Value;
        public readonly Node[] Children;

        Node(Kinds kind, Tags tag, object value, params Node[] children) :
            this(Reserve(), kind, tag, value, children)
        { }

        Node(uint identifier, Kinds kind, Tags tag, object value, params Node[] children)
        {
            Identifier = identifier;
            Kind = kind;
            Tag = tag;
            Value = value;
            Children = children;
        }

        public Node With(params Node[] children) => Children == children ? this : new Node(Identifier, Kind, Tag, Value, children);
        public Node With(uint identifier) => Identifier == identifier ? this : new Node(identifier, Kind, Tag, Value, Children);

        public bool Equals(Node other)
        {
            if (other == null) return false;
            if (ReferenceEquals(this, other)) return true;
            if (Kind != other.Kind) return false;
            // Do not check tags since some tags may differentiate nodes that are otherwise equals.
            switch (Kind)
            {
                case Kinds.Null: return ReferenceEquals(Value, other.Value);
                case Kinds.Number: return this.AsDecimal() == other.AsDecimal();
                case Kinds.Boolean: return this.AsBool() == other.AsBool();
                case Kinds.String: return this.AsString() == other.AsString();
                case Kinds.Type: return this.AsType() == other.AsType();
                case Kinds.Reference: return this.AsReference() == other.AsReference();
                default: return ReferenceEquals(Value, other.Value) && ArrayUtility.Equals(Children, other.Children);
            }
        }

        public override bool Equals(object obj) => Equals(obj as Node);

        public override int GetHashCode() =>
            Kind.GetHashCode() ^
            Value?.GetHashCode() ?? 0 ^
            ArrayUtility.GetHashCode(Children);

        public override string ToString()
        {
            switch (Kind)
            {
                case Kinds.Null: return "null";
                case Kinds.Boolean when this.TryBool(out var value): return value ? "true" : "false";
                case Kinds.String when this.TryString(out var value): return $@"""{value}""";
                case Kinds.Type when this.TryType(out var type): return type.FullFormat();
                case Kinds.Abstract when this.TryAbstract(out var type, out var value): return $"{value} ({type.FullFormat()})";
                case Kinds.Reference when this.TryReference(out var value): return $"${value}";
                case Kinds.Array: return $"[{string.Join<Node>(", ", this.Items())}]";
                case Kinds.Object: return $"{{{string.Join(", ", this.Members().Select(pair => $"{pair.key}: {pair.value}"))}}}";
                default: return Convert.ToString(Value, CultureInfo.InvariantCulture);
            }
        }
    }
}