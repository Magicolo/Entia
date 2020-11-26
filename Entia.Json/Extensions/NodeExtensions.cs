using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Entia.Core;

namespace Entia.Json
{
    public static partial class NodeExtensions
    {
        public static Node Map(this Node node, Func<Node, Node> map) =>
            node.Children.Length == 0 ? node : node.With(node.Children.Select(map));
        public static Node Map<TState>(this Node node, in TState state, Func<Node, TState, Node> map) =>
            node.Children.Length == 0 ? node : node.With(node.Children.Select(state, map));
        public static Node Filter(this Node node, Func<Node, bool> filter) =>
            node.Children.Length == 0 ? node : node.With(node.Children.Where(filter).ToArray());
        public static Node Filter<TState>(this Node node, in TState state, Func<Node, TState, bool> filter) =>
            node.Children.Length == 0 ? node : node.With(node.Children.Where(state, filter).ToArray());

        public static bool Has(this Node node, Node child) => node.TryIndex(child, out _);
        public static bool TryIndex(this Node node, Node child, out int index)
        {
            for (int i = 0; i < node.Children.Length; i++)
            {
                if (ReferenceEquals(node.Children[i], child))
                {
                    index = i;
                    return true;
                }
            }
            index = default;
            return false;
        }

        public static Node Add(this Node node, Node child) => node.With(node.Children.Append(child));
        public static Node Add(this Node node, params Node[] children) => node.With(node.Children.Append(children));
        public static Node AddAt(this Node node, int index, Node child) => node.With(node.Children.Insert(index, child));
        public static Node AddAt(this Node node, int index, params Node[] children) => node.With(node.Children.Insert(index, children));

        public static Node Remove(this Node node, Node child) => node.With(node.Children.Remove(child));
        public static Node Remove(this Node node, params Node[] children) => children.Aggregate(node, (current, child) => current.Remove(child));
        public static Node RemoveAt(this Node node, int index) => node.With(node.Children.RemoveAt(index));
        public static Node RemoveAt(this Node node, int index, int count) => node.With(node.Children.RemoveAt(index, count));
        public static Node Clear(this Node node) => node.With(Array.Empty<Node>());

        public static Node Replace(this Node node, Node child, Node replacement) =>
            node.TryIndex(child, out var index) ? node.ReplaceAt(index, replacement) : node;

        public static Node ReplaceAt(this Node node, int index, Node replacement)
        {
            if (index < 0 || index >= node.Children.Length ||
                ReferenceEquals(node.Children[index], replacement))
                return node;

            var children = node.Children.Clone() as Node[];
            children[index] = replacement;
            return node.With(children);
        }

        public static Node ReplaceAt(this Node node, int index, params Node[] replacements)
        {
            static bool AreEqual(Node[] source, Node[] target, int index)
            {
                for (int i = 0; i < target.Length; i++)
                {
                    if (ReferenceEquals(source[index + i], target[i])) continue;
                    return false;
                }
                return true;
            }

            if (index < 0 || index + replacements.Length > node.Children.Length ||
                AreEqual(node.Children, replacements, index))
                return node;

            var children = node.Children.Clone() as Node[];
            Array.Copy(replacements, 0, children, index, replacements.Length);
            return node.With(children);
        }

        public static IEnumerable<Node> Family(this Node node)
        {
            yield return node;
            foreach (var descendant in node.Descendants()) yield return descendant;
        }

        public static IEnumerable<Node> Descendants(this Node node)
        {
            foreach (var child in node.Children)
            {
                yield return child;
                foreach (var descendant in child.Descendants()) yield return descendant;
            }
        }

        public static bool Is(this Node node, Node.Kinds kind) => node.Kind == kind;
        public static bool Has(this Node node, Node.Tags tags) => (node.Tag & tags) == tags;
        public static bool HasPlain(this Node node) => node.Has(Node.Tags.Plain);
        public static bool HasSpecial(this Node node) => node.Has(Node.Tags.Special);
        public static bool IsNull(this Node node) => node.Is(Node.Kinds.Null);
        public static bool IsString(this Node node) => node.Is(Node.Kinds.String);
        public static bool IsBoolean(this Node node) => node.Is(Node.Kinds.Boolean);
        public static bool IsNumber(this Node node) => node.Is(Node.Kinds.Number);
        public static bool IsArray(this Node node) => node.Is(Node.Kinds.Array);
        public static bool IsObject(this Node node) => node.Is(Node.Kinds.Object) && node.Children.Length % 2 == 0;
        public static bool IsType(this Node node) => node.Is(Node.Kinds.Type);
        public static bool IsReference(this Node node) => node.Is(Node.Kinds.Reference);
        public static bool IsAbstract(this Node node) => node.Is(Node.Kinds.Abstract) && node.Children.Length == 2;

        public static bool TryString(this Node node, out string value)
        {
            if (node.IsString())
            {
                value = (string)node.Value;
                return true;
            }
            value = default;
            return false;
        }

        public static bool TryBool(this Node node, out bool value)
        {
            if (node.IsBoolean())
            {
                value = (bool)node.Value;
                return true;
            }
            value = default;
            return false;
        }

        public static bool TryChar(this Node node, out char value)
        {
            if (node.IsNumber())
            {
                value = (char)Math.Max(((decimal)node.Value % char.MaxValue), 0m);
                return true;
            }
            else if (node.TryString(out var @string))
                return @string.TryFirst(out value);
            value = default;
            return false;
        }

        public static bool TrySByte(this Node node, out sbyte value)
        {
            if (node.IsNumber())
            {
                value = (sbyte)((decimal)node.Value % sbyte.MaxValue);
                return true;
            }
            value = default;
            return false;
        }

        public static bool TryByte(this Node node, out byte value)
        {
            if (node.IsNumber())
            {
                value = (byte)Math.Max(((decimal)node.Value % byte.MaxValue), 0);
                return true;
            }
            value = default;
            return false;
        }

        public static bool TryShort(this Node node, out short value)
        {
            if (node.IsNumber())
            {
                value = (short)((decimal)node.Value % short.MaxValue);
                return true;
            }
            value = default;
            return false;
        }

        public static bool TryUShort(this Node node, out ushort value)
        {
            if (node.IsNumber())
            {
                value = (ushort)Math.Max(((decimal)node.Value % ushort.MaxValue), 0);
                return true;
            }
            value = default;
            return false;
        }

        public static bool TryInt(this Node node, out int value)
        {
            if (node.IsNumber())
            {
                value = (int)((decimal)node.Value % int.MaxValue);
                return true;
            }
            value = default;
            return false;
        }

        public static bool TryUInt(this Node node, out uint value)
        {
            if (node.IsNumber())
            {
                value = (uint)Math.Max(((decimal)node.Value % uint.MaxValue), 0m);
                return true;
            }
            value = default;
            return false;
        }

        public static bool TryLong(this Node node, out long value)
        {
            if (node.IsNumber())
            {
                value = (long)((decimal)node.Value % long.MaxValue);
                return true;
            }
            value = default;
            return false;
        }

        public static bool TryULong(this Node node, out ulong value)
        {
            if (node.IsNumber())
            {
                value = (ulong)Math.Max(((decimal)node.Value % ulong.MaxValue), 0m);
                return true;
            }
            value = default;
            return false;
        }

        public static bool TryFloat(this Node node, out float value)
        {
            if (node.IsNumber())
            {
                value = (float)(decimal)node.Value;
                return true;
            }
            value = default;
            return false;
        }

        public static bool TryDouble(this Node node, out double value)
        {
            if (node.IsNumber())
            {
                value = (double)(decimal)node.Value;
                return true;
            }
            value = default;
            return false;
        }

        public static bool TryDecimal(this Node node, out decimal value)
        {
            if (node.IsNumber())
            {
                value = (decimal)node.Value;
                return true;
            }
            value = default;
            return false;
        }

        public static bool TryEnum<T>(this Node node, out T value) where T : struct, Enum
        {
            if (node.TryLong(out var @long))
            {
                value = (T)Enum.ToObject(typeof(T), @long);
                return true;
            }
            else if (node.TryString(out var @string))
                return Enum.TryParse(@string, out value);
            value = default;
            return false;
        }

        public static bool TryEnum(this Node node, Type type, out Enum value)
        {
            if (node.TryLong(out var @long))
            {
                value = (Enum)Enum.ToObject(type, @long);
                return true;
            }
            else if (node.TryString(out var @string))
            {
                try
                {
                    value = (Enum)Enum.Parse(type, @string);
                    return true;
                }
                catch { }
            }
            value = default;
            return false;
        }

        public static bool TryType(this Node node, out Type value)
        {
            if (node.IsType())
            {
                value = (Type)node.Value;
                return true;
            }
            value = default;
            return false;
        }

        public static bool TryReference(this Node node, out uint value)
        {
            if (node.IsReference())
            {
                value = (uint)node.Value;
                return true;
            }
            value = default;
            return false;
        }

        public static bool TryAbstract(this Node node, out Type type, out Node value)
        {
            if (node.IsAbstract() && node.Children[0].TryType(out type))
            {
                value = node.Children[1];
                return true;
            }
            type = default;
            value = default;
            return false;
        }

        public static string AsString(this Node node, string @default = default) => node.TryString(out var value) ? value : @default;
        public static bool AsBool(this Node node, bool @default = default) => node.TryBool(out var value) ? value : @default;
        public static char AsChar(this Node node, char @default = default) => node.TryChar(out var value) ? value : @default;
        public static sbyte AsSByte(this Node node, sbyte @default = default) => node.TrySByte(out var value) ? value : @default;
        public static byte AsByte(this Node node, byte @default = default) => node.TryByte(out var value) ? value : @default;
        public static short AsShort(this Node node, short @default = default) => node.TryShort(out var value) ? value : @default;
        public static ushort AsUShort(this Node node, ushort @default = default) => node.TryUShort(out var value) ? value : @default;
        public static int AsInt(this Node node, int @default = default) => node.TryInt(out var value) ? value : @default;
        public static uint AsUInt(this Node node, uint @default = default) => node.TryUInt(out var value) ? value : @default;
        public static long AsLong(this Node node, long @default = default) => node.TryLong(out var value) ? value : @default;
        public static ulong AsULong(this Node node, ulong @default = default) => node.TryULong(out var value) ? value : @default;
        public static float AsFloat(this Node node, float @default = default) => node.TryFloat(out var value) ? value : @default;
        public static double AsDouble(this Node node, double @default = default) => node.TryDouble(out var value) ? value : @default;
        public static decimal AsDecimal(this Node node, decimal @default = default) => node.TryDecimal(out var value) ? value : @default;
        public static T AsEnum<T>(this Node node, T @default = default) where T : struct, Enum => node.TryEnum<T>(out var value) ? value : @default;
        public static Enum AsEnum(this Node node, Type type, Enum @default = default) => node.TryEnum(type, out var value) ? value : @default;
        public static Type AsType(this Node node, Type @default = default) => node.TryType(out var value) ? value : @default;
        public static uint AsReference(this Node node, uint @default = default) => node.TryReference(out var value) ? value : @default;
    }
}