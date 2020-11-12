using System;
using System.Globalization;
using Entia.Check;
using Entia.Core;
using static Entia.Check.Generator;

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
            Rational.Inverse().Map(Node.Number),
            All(Rational, Rational).Map(values => Node.Number(values[0] / values[1])));
        static readonly Generator<Node> _string = Any(
            Any(Node.EmptyString, Enumeration().Map(Node.String), Flags().Map(Node.String)),
            Any(ASCII, Letter, Digit, Character, '\\', '\"', '/', '\t', '\f', '\b', '\n', '\r').String(Range(100)).Map(Node.String));
        static readonly Generator<Node> _type = Types.Type.Map(Node.Type);
        static readonly Generator<Node> _array = Lazy(() => _node).Repeat(Range(10).Attenuate(10)).Map(Node.Array);
        static readonly Generator<Node> _object = All(_string, Lazy(() => _node)).Repeat(Range(10).Attenuate(10)).Map(nodes => Node.Object(nodes.Flatten()));
        static readonly Generator<Node> _leaf = Any(Node.Null, Node.EmptyArray, Node.EmptyObject, _boolean, _string, _number, _type);
        static readonly Generator<Node> _branch = Any(_array, _object).Depth();
        static readonly Generator<Node> _node = Any(_leaf, _branch);

        public static void Run()
        {
            _string.Check("Generate/parse symmetry for String nodes.");
            _number.Check("Generate/parse symmetry for Number nodes.");
            _type.Check("Generate/parse symmetry for Type nodes.");
            _node.Check("Generate/parse symmetry for Root nodes.");

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

            _number.And(_settings).Map(pair =>
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
            // BUG: Generate/Parse are not symmetric for very large or very small rational numbers.
        }

        static Failure<T>[] Check<T>(this Generator<T> generator, string name, Func<T, bool> prove) =>
            generator.Prove(name, prove).Log(name).Check();

        static Failure<(Node, string, Result<Node>, Result<string>)>[] Check(this Generator<Node> generator, string name) =>
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