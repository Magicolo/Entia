using System;
using System.Runtime.CompilerServices;
using System.Text;
using Entia.Core;
using Entia.Json.Converters;

namespace Entia.Json
{
    public static partial class Serialization
    {
        const char _A = 'A', _B = 'B', _C = 'C', _D = 'D', _E = 'E', _F = 'F', _N = 'N', _T = 'T';
        const char _a = 'a', _b = 'b', _c = 'c', _d = 'd', _e = 'e', _f = 'f', _i = 'i', _k = 'k';
        const char _l = 'l', _n = 'n', _r = 'r', _s = 's', _t = 't', _u = 'u', _v = 'v';
        const char _0 = '0', _1 = '1', _2 = '2', _3 = '3', _4 = '4', _5 = '5', _6 = '6', _7 = '7', _8 = '8', _9 = '9';
        const char _plus = '+', _minus = '-', _comma = ',', _dot = '.', _colon = ':', _quote = '"', _backSlash = '\\', _frontSlash = '/', _dollar = '$';
        const char _openCurly = '{', _closeCurly = '}', _openSquare = '[', _closeSquare = ']';
        const char _tab = '\t', _space = ' ', _line = '\n', _return = '\r', _back = '\b', _feed = '\f';

        static readonly Node[] _empty = { };
        static readonly decimal[] _positives = { 1e0m, 1e1m, 1e2m, 1e3m, 1e4m, 1e5m, 1e6m, 1e7m, 1e8m, 1e9m, 1e10m, 1e11m, 1e12m, 1e13m, 1e14m, 1e15m, 1e16m, 1e17m, 1e18m, 1e19m, 1e20m, 1e21m, 1e22m, 1e23m, 1e24m, 1e25m, 1e26m, 1e27m, 1e28m, };
        static readonly decimal[] _negatives = { 1e-0m, 1e-1m, 1e-2m, 1e-3m, 1e-4m, 1e-5m, 1e-6m, 1e-7m, 1e-8m, 1e-9m, 1e-10m, 1e-11m, 1e-12m, 1e-13m, 1e-14m, 1e-15m, 1e-16m, 1e-17m, 1e-18m, 1e-19m, 1e-20m, 1e-21m, 1e-22m, 1e-23m, 1e-24m, 1e-25m, 1e-26m, 1e-27m, 1e-28m, };

        static unsafe Result<Node> Parse(string text, in FromContext context)
        {
            if (string.IsNullOrWhiteSpace(text)) return Result.Failure("Expected valid json.");

            var index = 0;
            var depth = 0;
            var nodes = new Node[64];
            var brackets = new (int index, Node.Tags tags)[8];
            var builder = default(StringBuilder);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void Push(Node node)
            {
                if (index >= nodes.Length)
                {
                    var resized = new Node[nodes.Length * 2];
                    Array.Copy(nodes, 0, resized, 0, nodes.Length);
                    nodes = resized;
                }
                brackets[depth].tags |= node.Tag & Node.Tags.Special;
                nodes[index++] = node;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            Node[] Pop(int count)
            {
                if (count == 0) return _empty;
                index -= count;
                var popped = new Node[count];
                Array.Copy(nodes, index, popped, 0, count);
                return popped;
            }

            fixed (char* pointer = text)
            {
                var head = pointer;
                var tail = pointer + text.Length;
                while (head != tail)
                {
                    switch (*head++)
                    {
                        case _n:
                        case _N:
                            if (tail - head >= 3 && *head++ == _u && *head++ == _l && *head++ == _l)
                                Push(Node.Null);
                            else
                                return Result.Failure($"Expected 'null' at index '{Index(pointer, head) - 1}'.");
                            break;
                        case _t:
                        case _T:
                            if (tail - head >= 3 && *head++ == _r && *head++ == _u && *head++ == _e)
                                Push(Node.True);
                            else
                                return Result.Failure($"Expected 'true' at index '{Index(pointer, head) - 1}'.");
                            break;
                        case _f:
                        case _F:
                            if (tail - head >= 4 && *head++ == _a && *head++ == _l && *head++ == _s && *head++ == _e)
                                Push(Node.False);
                            else
                                return Result.Failure($"Expected 'false' at index '{Index(pointer, head) - 1}'.");
                            break;
                        case _minus: Push(ParseNumber(ref head, tail, 0, true)); break;
                        case _0: Push(ParseNumber(ref head, tail, 0, false)); break;
                        case _1: Push(ParseNumber(ref head, tail, 1, false)); break;
                        case _2: Push(ParseNumber(ref head, tail, 2, false)); break;
                        case _3: Push(ParseNumber(ref head, tail, 3, false)); break;
                        case _4: Push(ParseNumber(ref head, tail, 4, false)); break;
                        case _5: Push(ParseNumber(ref head, tail, 5, false)); break;
                        case _6: Push(ParseNumber(ref head, tail, 6, false)); break;
                        case _7: Push(ParseNumber(ref head, tail, 7, false)); break;
                        case _8: Push(ParseNumber(ref head, tail, 8, false)); break;
                        case _9: Push(ParseNumber(ref head, tail, 9, false)); break;
                        case _quote: Push(ParseString(ref builder, ref head, tail)); break;
                        case _openCurly:
                        case _openSquare: Serialization.Push(ref brackets, ref depth, (index, Node.Tags.None)); break;
                        case _closeCurly:
                            if (TryPop(brackets, ref depth, out var members))
                            {
                                var count = index - members.index;
                                if (count % 2 == 0) Push(Node.Object(Pop(count), members.tags));
                                else return Result.Failure($"Expected all keys to be paired with a value around index '{Index(pointer, head) - 1}'.");
                                break;
                            }
                            else
                                return Result.Failure($"Expected balanced curly bracket at index '{Index(pointer, head) - 1}'.");
                        case _closeSquare:
                            if (TryPop(brackets, ref depth, out var items))
                            {
                                Push(Node.Array(Pop(index - items.index), items.tags));
                                break;
                            }
                            else
                                return Result.Failure($"Expected balanced square bracket at index '{Index(pointer, head) - 1}'.");
                        case _space: case _tab: case _line: case _return: case _comma: case _colon: break;
                        default: return Result.Failure($"Expected character '{head[1]}' at index '{Index(pointer, head) - 1}' to be valid.");
                    }
                }
            }

            if (depth > 0) return Result.Failure("Expected brackets to be balanced.");
            else if (index > 1) return Result.Failure("Expected all child nodes to be consumed.");
            else if (nodes[0] is Node root)
            {
                Unwrap(ref root, context);
                return root;
            }
            else return Result.Failure("Expected valid json.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Push<T>(ref T[] array, ref int count, T item)
        {
            var index = ++count;
            if (index >= array.Length) Array.Resize(ref array, array.Length * 2);
            array[index] = item;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool TryPop<T>(T[] array, ref int count, out T item)
        {
            if (count < 0)
            {
                item = default;
                return false;
            }
            item = array[count--];
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static unsafe int Index(char* head, char* tail) => (int)(tail - head);

        static unsafe Node ParseString(ref StringBuilder builder, ref char* head, char* tail)
        {
            var start = head;
            var first = *head++;
            if (first == _quote) return Node.EmptyString;
            if (first == _backSlash) return ParseEscapedString(ref builder, ref head, tail, ref start);

            var second = *head++;
            if (second == _quote) return Node.String(first);
            if (second == _backSlash) return ParseEscapedString(ref builder, ref head, tail, ref start);
            if (first == _dollar)
            {
                var third = *head++;
                if (third == _quote) return Node.Dollar(second);
                if (third == _backSlash) return ParseEscapedString(ref builder, ref head, tail, ref start);
            }

            while (head != tail)
            {
                var current = *head++;
                if (current == _quote) break;
                if (current == _backSlash) return ParseEscapedString(ref builder, ref head, tail, ref start);
            }
            return Node.String(new string(start, 0, Index(start, head) - 1), Node.Tags.Plain);

            static unsafe Node ParseEscapedString(ref StringBuilder builder, ref char* head, char* tail, ref char* start)
            {
                if (builder == null) builder = new StringBuilder(256);
                else builder.Clear();

                AppendUnescaped(builder, ref head, tail, ref start);
                while (head != tail)
                {
                    var current = *head++;
                    if (current == _backSlash) AppendUnescaped(builder, ref head, tail, ref start);
                    else if (current == _quote) break;
                }
                builder.Append(start, Index(start, head) - 1);
                return Node.String(builder.ToString(), Node.Tags.None);
            }

            static unsafe void AppendUnescaped(StringBuilder builder, ref char* head, char* tail, ref char* start)
            {
                builder.Append(start, Index(start, head) - 1);
                switch (*head++)
                {
                    case _n: builder.Append(_line); break;
                    case _b: builder.Append(_back); break;
                    case _f: builder.Append(_feed); break;
                    case _r: builder.Append(_return); break;
                    case _t: builder.Append(_tab); break;
                    case _quote: builder.Append(_quote); break;
                    case _backSlash: builder.Append(_backSlash); break;
                    case _frontSlash: builder.Append(_frontSlash); break;
                    case _u:
                        if (tail - head >= 4)
                        {
                            var value =
                                (FromHex(*head++) << 12) |
                                (FromHex(*head++) << 8) |
                                (FromHex(*head++) << 4) |
                                FromHex(*head++);
                            builder.Append((char)value);
                        }
                        break;
                }
                start = head;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static int FromHex(char character)
            {
                if (character >= _0 && character <= _9) return character - _0;
                else if (character >= _a && character <= _f) return character - _a + 10;
                else if (character >= _A && character <= _F) return character - _A + 10;
                else return 0;
            }
        }

        static unsafe Node ParseNumber(ref char* head, char* tail, ulong low, bool sign)
        {
            const ulong maximumLow = (ulong.MaxValue - 9) / 10;
            const ulong maximumHigh = (uint.MaxValue - 9) / 10;

            var integer = 0u;
            var fraction = 0u;
            var high = 0u;

            // Consume leading 0 but do not add it to the integer count.
            if (sign && TryDigit(ref head, tail, out var digit) && digit > 0)
            {
                integer++;
                low = digit;
            }

            // Integer part.
            while (TryDigit(ref head, tail, out digit))
            {
                integer++;
                Digit(ref low, ref high, digit);
            }

            // Fractional part.
            if (head != tail && *head == _dot)
            {
                head++;
                while (TryDigit(ref head, tail, out digit))
                {
                    // Discard some fractional precision rather than overflowing the high bits.
                    if (high < maximumHigh)
                    {
                        fraction++;
                        Digit(ref low, ref high, digit);
                    }
                }
            }

            // Exponent part.
            if (head != tail && *head == _e || *head == _E)
            {
                head++;
                var exponent = 0u;
                var positive = true;

                // Consume exponent sign.
                if (head != tail)
                {
                    var current = *head;
                    if (current == _minus)
                    {
                        head++;
                        positive = false;
                    }
                    else if (current == _plus) head++;
                }

                while (TryDigit(ref head, tail, out digit))
                    exponent = exponent * 10 + digit;

                // Adjust the dot, if possible.
                if (positive)
                {
                    // Check for overflow.
                    if (integer + exponent >= 28) return 0m;
                    var shift = Math.Min(fraction, exponent);
                    integer += shift;
                    fraction -= shift;
                    exponent -= shift;
                }
                else
                {
                    var shift = Math.Min(integer, exponent);
                    integer -= shift;
                    fraction += shift;
                    exponent -= shift;
                }

                // Exponent is too large.
                if (exponent >= 28) return 0m;
                var power = positive ? _positives[exponent] : _negatives[exponent];
                var value = Value(low, high, fraction, sign);
                return Node.Number(value * power);
            }
            else return Node.Number(Value(low, high, fraction, sign));

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static bool TryDigit(ref char* head, char* tail, out uint digit)
            {
                if (head != tail)
                {
                    var current = *head;
                    if (current >= _0 && current <= _9)
                    {
                        head++;
                        digit = (uint)(current - _0);
                        return true;
                    }
                }

                digit = default;
                return false;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static void Digit(ref ulong low, ref uint high, uint digit)
            {
                if (low < maximumLow)
                {
                    low = low * 10 + digit;
                    high *= 10;
                }
                else
                {
                    var initial = low;
                    var carry = (uint)(low >> 61);
                    high = high * 10 + carry;
                    low <<= 3;
                    {
                        var previous = low;
                        low += initial;
                        if (previous > low) high += 1;
                    }
                    {
                        var previous = low;
                        low += initial;
                        if (previous > low) high += 1;
                    }
                    {
                        var previous = low;
                        low += digit;
                        if (previous > low) high += 1;
                    }
                }
            }


            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static decimal Value(ulong low, ulong high, uint fraction, bool sign) =>
                new decimal((int)low, (int)(low >> 32), (int)high, sign, (byte)fraction);
        }

        static void Unwrap(ref Node node, in FromContext context)
        {
            if (node.HasSpecial())
            {
                if (context.Settings.Features.HasAll(Features.Reference))
                {
                    var identifiers = new uint?[8];
                    UnwrapIdentified(ref node, ref identifiers);
                    UnwrapReferences(ref node, identifiers);
                }
                if (context.Settings.Features.HasAll(Features.Abstract)) ConvertTypes(ref node, context);
            }

            static void UnwrapIdentified(ref Node node, ref uint?[] identifiers)
            {
                if (node.IsObject() &&
                    node.Children.Length == 4 &&
                    node.Children[0] == Node.DollarI &&
                    node.Children[1].TryInt(out var index) &&
                    node.Children[2] == Node.DollarV)
                {
                    var value = node.Children[3];
                    UnwrapIdentified(ref value, ref identifiers);
                    ArrayUtility.Ensure(ref identifiers, index + 1);
                    identifiers[index] = node.Identifier;
                    node = value.With(node.Identifier);
                }
                else if (node.HasSpecial())
                    for (int i = 0; i < node.Children.Length; i++) UnwrapIdentified(ref node.Children[i], ref identifiers);
            }

            static void UnwrapReferences(ref Node node, uint?[] identifiers)
            {
                if (node.IsObject() &&
                    node.Children.Length == 2 &&
                    node.Children[0] == Node.DollarR &&
                    node.Children[1].TryInt(out var index))
                {
                    var reference =
                        index >= 0 && index < identifiers.Length && identifiers[index] is uint identifier ?
                        Node.Reference(identifier) : Node.Null;
                    node = reference.With(node.Identifier);
                }
                else if (node.HasSpecial())
                    for (int i = 0; i < node.Children.Length; i++) UnwrapReferences(ref node.Children[i], identifiers);
            }

            static Node ConvertType(Node node, in FromContext context) =>
                Node.Type(context.Convert<Type>(node, JsonType.Instance, JsonType.Instance));

            static void ConvertTypes(ref Node node, in FromContext context)
            {
                if (node.IsObject() && node.Children.Length == 2 && node.Children[0] == Node.DollarT)
                    node = ConvertType(node.Children[1], context).With(node.Identifier);
                else if (node.IsObject() && node.Children.Length == 4 && node.Children[0] == Node.DollarT && node.Children[2] == Node.DollarV)
                {
                    var type = node.Children[1];
                    var value = node.Children[3];
                    ConvertTypes(ref value, context);
                    node = Node.Abstract(ConvertType(type, context).With(type.Identifier), value).With(node.Identifier);
                }
                else if (node.HasSpecial())
                    for (int i = 0; i < node.Children.Length; i++) ConvertTypes(ref node.Children[i], context);
            }
        }
    }
}