using System;
using System.Globalization;
using Entia.Check;
using Entia.Core;
using static Entia.Check.Generator;

namespace Entia.Json
{
    public static class Checks
    {
        static readonly Settings _settings = Settings.Default.With(Features.All);
        static readonly Generator<Enum> _enumeration = Types.Enumeration.Bind(Enumeration);
        static readonly Generator<Node> _boolean = Any(
            Constant(Node.True),
            Constant(Node.False),
            Generator.Boolean.Map(Node.Boolean));
        static readonly Generator<Node> _number = Any(
            Any(Constant(Node.Zero), Constant(Node.Number(float.NaN)), Infinity.Map(Node.Number)),
            Any(_enumeration.Map(Node.Number), Character.Map(Node.Number)),
            Integer.Map(Node.Number),
            Rational.Map(Node.Number),
            All(Rational, Rational).Map(values => Node.Number(values[0] / values[1])));
        static readonly Generator<Node> _string = Any(
            Any(Constant(Node.EmptyString), _enumeration.Map(Node.String)),
            Any(ASCII, Letter, Digit, Any('\\', '\"', '/', '\t', '\f', '\b', '\n', '\r'), Character).String(Range(100)).Map(Node.String));
        static readonly Generator<Node> _type = Types.Type.Map(Node.Type);
        static readonly Generator<Node> _array = Lazy(() => _node).Repeat(Range(10).Attenuate(10)).Map(Node.Array);
        static readonly Generator<Node> _object = All(_string, Lazy(() => _node)).Repeat(Range(10).Attenuate(10)).Map(nodes => Node.Object(nodes.Flatten()));
        static readonly Generator<Node> _leaf = Any(
            Constant(Node.Null),
            Constant(Node.EmptyArray),
            Constant(Node.EmptyObject),
            _boolean,
            _string,
            _number,
            _type);
        static readonly Generator<Node> _branch = Any(_array, _object).Depth();
        static readonly Generator<Node> _node = Any(_leaf, _branch);

        public static void Run()
        {
            _string.Check("Generate/parse symmetry for String nodes.");
            _number.Check("Generate/parse symmetry for Number nodes.");
            _type.Check("Generate/parse symmetry for Type nodes.");
            _node.Check("Generate/parse symmetry for Root nodes.");

            _number.Map(node =>
            {
                var generated = Serialization.Generate(node, _settings);
                var parsed = Serialization.Parse(generated, _settings);
                var system = node.IsNull() ? node : Node.Number(double.Parse(generated, CultureInfo.InvariantCulture));
                return (node, generated, parsed, system);
            }).Check("Generate/parse/double.Parse symmetry for Number nodes.", tuple =>
                tuple.node == tuple.parsed &&
                tuple.node == tuple.system &&
                tuple.parsed == tuple.system);

            _node.Map(node =>
            {
                string Generate(Node node) => Serialization.Generate(node.Map(child => Generate(child)), _settings);
                Node Parse(string json) => Serialization.Parse(json, _settings).Or(Node.Null).Map(child => Parse(child.AsString()));
                var generated = Generate(node);
                var parsed = Parse(generated);
                return (node, generated, parsed);
            }).Check("Generate/parse symmetry for nested json.", tuple => tuple.node == tuple.parsed);

            // TODO: Add test for parsing foreign jsons
            // TODO: Add test for comparing output with Json.Net and .Net Json parser.
            // BUG: Generate/Parse are not symmetric for very large or very small rational numbers.
        }

        static Failure<T>[] Check<T>(this Generator<T> generator, string name, Func<T, bool> prove) =>
            generator.Prove(name, prove).Log(name).Check();

        static Failure<(Node, string, Result<Node>)>[] Check(this Generator<Node> generator, string name) => generator
            .Map(node =>
            {
                var json = Serialization.Generate(node, _settings);
                var result = Serialization.Parse(json, _settings);
                return (node, json, result);
            })
            .Check(name, values => values.node == values.result);
    }
}