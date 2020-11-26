using System;
using System.Linq;
using System.Globalization;
using Entia.Check;
using Entia.Core;
using static Entia.Check.Generator;
using System.Collections.Generic;

namespace Entia.Json
{
    public static class Checks
    {
        static readonly Generator<Settings> _settings = Flags<Features>().And(Flags<Formats>())
            .Map(pair => Settings.Default.With(pair.Item1 | Features.Abstract, pair.Item2));
        static readonly Generator<Node> _boolean = Any(Node.True, Node.False, Generator.Boolean.Map(Node.Boolean));
        static readonly Generator<Node> _number = Any(
            Any(Node.Zero, Node.Number(float.NaN), Node.Number(double.NaN), Infinity.Map(Node.Number)),
            Any(Enumeration().Map(Node.Number), Flags().Map(Node.Number), Character.Map(Node.Number)),
            Integer.Map(Node.Number),
            Rational.Map(Node.Number),
            Rational.Inverse().Map(Node.Number), // Small numbers.
            Range(decimal.MinValue).Size(size => Math.Pow(size, 20)).Map(Node.Number), // Very large negative numbers.
            Range(decimal.MaxValue).Size(size => Math.Pow(size, 20)).Map(Node.Number), // Very large positive numbers.
            Range(decimal.MinValue).Size(size => Math.Pow(size, 20)).Inverse().Map(Node.Number), // Very small negative numbers.
            Range(decimal.MaxValue).Size(size => Math.Pow(size, 20)).Inverse().Map(Node.Number), // Very small positive numbers.
            All(Rational, Rational).Map(values => Node.Number(values[0] / values[1])));
        static readonly Generator<Node> _string = Any(
            Any(Node.EmptyString, Enumeration().Map(Node.String), Flags().Map(Node.String)),
            Any(ASCII, Letter, Digit).String(Range(100)).Map(Node.String),
            Any(Character).String(Range(100)).Map(Node.String),
            Any(Character, ASCII, Letter, Digit, '\\', '\"', '/', '\t', '\f', '\b', '\n', '\r').String(Range(100)).Map(Node.String));
        static readonly Generator<Node> _type = Types.Type.Map(Node.Type);
        static readonly Generator<Node> _array = Lazy(() => _node).Repeat(Range(10).Attenuate(10)).Map(Node.Array);
        static readonly Generator<Node> _object = All(_string, Lazy(() => _node))
            .Repeat(Range(10).Attenuate(10))
            .Map(nodes => Node.Object(nodes.DistinctBy(pair => pair[0].AsString()).ToArray().Flatten()));
        static readonly Generator<Node> _leaf = Any(Node.Null, Node.EmptyArray, Node.EmptyObject, _boolean, _string, _number, _type);
        static readonly Generator<Node> _branch = Any(_array, _object).Depth();
        static readonly Generator<Node> _node = Any(_leaf, _branch);

        public static void Run()
        {
            _node.Shallow().Check("Operations on nodes.", node =>
            {
                return Prove().ToArray();

                IEnumerable<Property> Prove()
                {
                    yield return ("IsNull", node.IsNull() == (node == Node.Null));
                    yield return ("IsNumber", (node.IsNumber() && node == 0) == (node == Node.Zero));
                    yield return ("IsBoolean", node.IsBoolean() == (node == Node.True || node == Node.False));
                    yield return ("IsString", node == Node.EmptyString || node.IsString() == (node.TryString(out var value) && value.Any()));
                    yield return ("IsObject", node == Node.EmptyObject || node.IsObject() == node.Members().Any());
                    yield return ("IsArray", node == Node.EmptyArray || node.IsArray() == node.Items().Any());
                    yield return ("HasPlain", node.HasPlain() ? node.IsString() : true);
                    yield return ("TryBool", node.IsBoolean() == node.TryBool(out _));
                    yield return ("TryString", node.IsString() == node.TryString(out _));
                    yield return ("AsString", node.IsString() == (node.AsString() != null));
                    yield return ("TryType", node.IsType() == node.TryType(out _));
                    yield return ("AsType", node.IsType() == (node.AsType() != null));
                    yield return ("TryChar", node.IsNumber() ? node.TryChar(out _) : true);
                    yield return ("TryByte", node.IsNumber() == node.TryByte(out _));
                    yield return ("TrySByte", node.IsNumber() == node.TrySByte(out _));
                    yield return ("TryShort", node.IsNumber() == node.TryShort(out _));
                    yield return ("TryUShort", node.IsNumber() == node.TryUShort(out _));
                    yield return ("TryInt", node.IsNumber() == node.TryInt(out _));
                    yield return ("TryUInt", node.IsNumber() == node.TryUInt(out _));
                    yield return ("TryLong", node.IsNumber() == node.TryLong(out _));
                    yield return ("TryULong", node.IsNumber() == node.TryULong(out _));
                    yield return ("TryFloat", node.IsNumber() == node.TryFloat(out _));
                    yield return ("TryDouble", node.IsNumber() == node.TryDouble(out _));
                    yield return ("TryDecimal", node.IsNumber() == node.TryDecimal(out _));
                    yield return ("TryEnum<T>", node.IsNumber() ? node.TryEnum<Node.Kinds>(out _) : true);
                    yield return ("TryEnum", node.IsNumber() ? node.TryEnum(typeof(Node.Kinds), out _) : true);
                    yield return ("AsEnum", node.IsNumber() ? node.AsEnum(typeof(Node.Kinds)) != null : true);

                    var children = node.Children;
                    for (int i = 0; i < children.Length; i++)
                    {
                        var child = children[i];
                        var removed = node.Remove(child);
                        var removedAt = node.RemoveAt(i);
                        var filtered = node.Filter(current => current != child);
                        yield return ("Has", node.Has(child));
                        yield return ("TryIndex", node.TryIndex(child, out _));
                        yield return ("Remove.Children", removed.Children.Length < children.Length);
                        yield return ("RemoveAt.Children", removedAt.Children.Length < children.Length);
                        yield return ("Filter.Has", !filtered.Has(child));
                        yield return ("Filter.TryIndex", !filtered.TryIndex(child, out _));
                        yield return ("Filter.Remove", filtered.Remove(child) == filtered);
                        yield return ("Filter.Filter", filtered.Filter(current => current != child) == filtered);
                    }

                    {
                        var removed = node.Remove(children);
                        var filtered = node.Filter(_ => false);
                        var cleared = node.Clear();
                        var mapped = node.Map(child => !child.AsBool());
                        yield return ("Remove == Filter", removed == filtered);
                        yield return ("Remove == Clear", removed == cleared);
                        yield return ("Filter == Clear", filtered == cleared);
                        yield return ("Remove.Children", removed.Children.None());
                        yield return ("Filter.Children", filtered.Children.None());
                        yield return ("Clear.Children", cleared.Children.None());
                        yield return ("Remove == node", node.Remove(node) == node && node.Remove() == node);
                        yield return ("RemoveAt == node", node.RemoveAt(-1) == node && node.RemoveAt(children.Length) == node);
                        yield return ("Filter == node", node.Filter(_ => true) == node);
                        yield return ("Map.None()", mapped.Children.Zip(children).None(pair => pair.Item1 == pair.Item2));
                        yield return ("Map.All()", mapped.Children.All(child => child.IsBoolean()));
                    }
                }
            });

            _object.Shallow().Check("Operations on Object nodes.", node =>
            {
                return Prove().ToArray();

                IEnumerable<Property> Prove()
                {
                    var members = node.Members().ToArray();
                    yield return ("IsObject", node.IsObject());
                    yield return ("TryMembers", node.TryMembers(out _));
                    yield return ("Members", node == Node.EmptyObject || members.Any());
                    yield return ("Members.Count", members.Length * 2 == node.Children.Length);

                    foreach (var pair in members)
                    {
                        var key = pair.key + "~";
                        var removed = node.RemoveMember(pair.key);
                        var filtered = node.FilterMember(pair.key, _ => false);
                        var added = node.AddMember(key, pair.value);
                        var replaced = node.AddMember(pair.key, !pair.value.AsBool());
                        var mapped = node.MapMember(pair.key, value => !value.AsBool());
                        var renamed = node.MapMember(pair.key, value => (key, value));
                        yield return ("HasMember", node.HasMember(pair.key));
                        yield return ("TryMember", node.TryMember(pair.key, out var value) && pair.value == value);
                        yield return ("RemoveMember == FilterMember", removed == filtered);
                        yield return ("RemoveMember != node", removed != node);
                        yield return ("FilterMember != node", filtered != node);
                        yield return ("MapMember != node", mapped != node);
                        yield return ("RemoveMember.HasMember", !removed.HasMember(pair.key));
                        yield return ("RemoveMember.TryMember", !removed.TryMember(pair.key, out _));
                        yield return ("FilterMember.HasMember", !filtered.HasMember(pair.key));
                        yield return ("FilterMember.TryMember", !filtered.TryMember(pair.key, out _));
                        yield return ("RemoveMember.RemoveMember", removed.RemoveMember(pair.key) == removed);
                        yield return ("RemoveMember.FilterMember", removed.FilterMember(pair.key, _ => false) == removed);
                        yield return ("FilterMember.FilterMember", filtered.FilterMember(pair.key, _ => false) == filtered);
                        yield return ("FilterMember.RemoveMember", filtered.RemoveMember(pair.key) == filtered);
                        yield return ("FilterMember == node", node.FilterMember(pair.key, _ => true) == node);
                        yield return ("AddMember == MapMember", replaced == mapped);
                        yield return ("AddMember != node", added != node && replaced != node);
                        yield return ("MapMember != node", mapped != node && renamed != node);
                        yield return ("AddMember.HasMember", added.HasMember(pair.key) && added.HasMember(key));
                        yield return ("AddMember.TryMember", added.TryMember(pair.key, out value) && value == pair.value && added.TryMember(key, out value) && value == pair.value);
                        yield return ("MapMember.HasMember", !renamed.HasMember(pair.key) && renamed.HasMember(key));
                        yield return ("MapMember.TryMember", !renamed.TryMember(pair.key, out _) && value == pair.value && renamed.TryMember(key, out value) && value == pair.value);
                        yield return ("AddMember == node", node.AddMember(pair.key, pair.value) == node);
                        yield return ("MapMember == node", node.MapMember(pair.key, _ => pair.value) == node && node.MapMember(pair.key, _ => pair) == node);
                    }

                    {
                        var filtered = node.FilterMembers(_ => false);
                        yield return ("FilterMembers.Members", filtered.Members().None());
                        yield return ("FilterMembers == node", node.FilterMembers(_ => true) == node);
                    }
                }
            });

            _array.Shallow().Check("Operations on Array nodes.", node =>
            {
                return Prove().ToArray();

                IEnumerable<Property> Prove()
                {
                    var items = node.Items();
                    yield return ("IsArray", node.IsArray());
                    yield return ("TryItems", node.TryItems(out _));
                    yield return ("Items", node == Node.EmptyArray || items.Any());
                    yield return ("Items.Count", items.Length == node.Children.Length);

                    for (int i = 0; i < items.Length; i++)
                    {
                        var item = items[i];
                        var removed = node.RemoveItem(i);
                        var filtered = node.FilterItem(i, _ => false);
                        var added = node.AddItem(i, item);
                        var replaced = node.ReplaceItem(i, !item.AsBool());
                        var mapped = node.MapItem(i, value => !value.AsBool());
                        yield return ("HasItem", node.HasItem(i));
                        yield return ("TryItem", node.TryItem(i, out var value) && item == value);
                        yield return ("RemoveItem == FilterItem", removed == filtered);
                        yield return ("RemoveItem != node", removed != node);
                        yield return ("FilterItem != node", filtered != node);
                        yield return ("FilterItem == node", node.FilterItem(i, _ => true) == node);
                        yield return ("ReplaceItem == MapItem", replaced == mapped);
                        yield return ("AddItem != node", added != node);
                        yield return ("ReplaceItem != node", replaced != node);
                        yield return ("MapItem != node", mapped != node);
                        yield return ("AddItem.HasItem", added.HasItem(i) && added.HasItem(i + 1));
                        yield return ("AddItem.TryItem", added.TryItem(i, out value) && value == item && added.TryItem(i + 1, out value) && value == item);
                        yield return ("ReplaceItem.HasItem", replaced.HasItem(i));
                        yield return ("MapItem.HasItem", mapped.HasItem(i));
                        yield return ("MapItem.TryItem", mapped.TryItem(i, out _));
                        yield return ("ReplaceItem == node", node.ReplaceItem(i, item) == node);
                        yield return ("MapItem == node", node.MapItem(i, _ => item) == node);
                    }

                    {
                        var filtered = node.FilterItems(_ => false);
                        yield return ("FilterItems.Items", filtered.Items().None());
                        yield return ("FilterItems == node", node.FilterItems(_ => true) == node);
                    }
                }
            });

            Integer.Map(value => (value, result: Serialization.Parse(value.ToString(CultureInfo.InvariantCulture)).Map(node => node.AsInt())))
                .Check("Parsing of integers.", pair => pair.value == pair.result);
            Rational.Map(value => (value, result: Serialization.Parse(value.ToString(CultureInfo.InvariantCulture)).Map(node => node.AsFloat())))
                .Check("Parsing of rationals.", pair => pair.value == pair.result);

            _number.And(_settings).Map(pair =>
            {
                var (node, settings) = pair;
                var generated = Serialization.Generate(node, settings);
                var parsed = Serialization.Parse(generated, settings);
                var system = node.IsNull() ? node : Node.Number(decimal.Parse(generated, CultureInfo.InvariantCulture));
                return (node, generated, parsed, system);
            }).Check("Generate/parse/decimal.Parse symmetry for Number nodes.", tuple =>
                tuple.node == tuple.parsed &&
                tuple.node == tuple.system &&
                tuple.parsed == tuple.system);

            _string.CheckSymmetry("Generate/parse symmetry for String nodes.");
            _number.CheckSymmetry("Generate/parse symmetry for Number nodes.");
            _type.CheckSymmetry("Generate/parse symmetry for Type nodes.");
            _node.CheckSymmetry("Generate/parse symmetry for nodes.");

            _node.And(_settings).Map(pair =>
            {
                var (node, settings) = pair;
                var generated = Generate(node);
                var parsed = Parse(generated);
                return (node, generated, parsed);

                string Generate(Node node) => Serialization.Generate(node.Map(child => Generate(child)), settings);
                Node Parse(string json) => Serialization.Parse(json, settings).Or(Node.Null).Map(child => Parse(child.AsString()));
            }).Check("Generate/parse symmetry for nested json.", tuple => tuple.node == tuple.parsed);

            // TODO: Add test for parsing foreign jsons
            // TODO: Add test for comparing output with Json.Net and .Net Json parser.
        }

        static Generator<T> Shallow<T>(this Generator<T> generator) => generator.Depth(3);

        static Failure<T>[] Check<T>(this Generator<T> generator, string name, Func<T, bool> prove) =>
            generator.Prove(name, prove).Log(name).Check();

        static Failure<T>[] Check<T>(this Generator<T> generator, string name, Prove<T> prove) =>
            generator.Prove(prove).Log(name).Check();

        static Failure<(Node, string, Result<Node>, Result<string>)>[] CheckSymmetry(this Generator<Node> generator, string name) =>
            generator.And(_settings)
                .Map(pair =>
                {
                    var (node1, settings) = pair;
                    var json1 = Serialization.Generate(node1, settings);
                    var node2 = Serialization.Parse(json1, settings);
                    var json2 = node2.Map(node => Serialization.Generate(node, settings));
                    return (node1, json1, node2, json2);
                })
                .Check(name, values => values.node1 == values.node2 && values.json1 == values.json2);
    }
}