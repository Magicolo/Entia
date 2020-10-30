using System;
using System.Globalization;
using System.Linq;
using Entia.Check;
using Entia.Core;
using static Entia.Check.Generator;

namespace Entia.Json.Test
{
    public static class Checks
    {
        enum Boba { A, B, C, D, E, F, G, H, I, J }

        static readonly Settings _settings = Settings.Default.With(Features.All);
        static readonly Random _random = new Random();

        static readonly Generator<Node> _boolean = Any(
            Constant(Node.True),
            Constant(Node.False),
            Generator.Boolean.Map(Node.Boolean));
        static readonly Generator<Node> _number = Any(
            Any(Constant(Node.Zero), Constant(Node.Number(float.NaN)), Infinity.Map(Node.Number)),
            Any(Enumeration<Boba>().Map(value => Node.Number(value)), Character.Map(Node.Number)),
            Integer.Map(Node.Number),
            Rational.Map(Node.Number),
            // Rational.Map(value => Node.Number(1f / value)),
            All(Rational, Rational).Map(values => Node.Number(values[0] / values[1])));
        static readonly Generator<Node> _string = Any(
            Constant(Node.EmptyString),
            Enumeration<Boba>().Map(value => Node.String(value)),
            Any(ASCII, Letter, Digit, Any('\\', '\"', '/', '\t', '\f', '\b', '\n', '\r'), Character).String(Range(100)).Map(Node.String));
        static readonly Generator<Node> _array = Lazy(() => _node).Repeat(Range(10).Attenuate(10)).Map(Node.Array);
        static readonly Generator<Node> _object = All(_string, Lazy(() => _node)).Repeat(Range(10).Attenuate(10)).Map(nodes => Node.Object(nodes.Flatten()));
        static readonly Generator<Node> _leaf = Any(
            Constant(Node.Null),
            Constant(Node.EmptyArray),
            Constant(Node.EmptyObject),
            _boolean,
            _string,
            _number);
        static readonly Generator<Node> _branch = Any(_array, _object).Depth();
        static readonly Generator<Node> _node = Any(_leaf, _branch);

        static readonly Generator<(Node, string, Result<Node>, Node)> _rational = _number.Map(node =>
            {
                var generated = Serialization.Generate(node);
                var parsed = Serialization.Parse(generated);
                return (node, generated, parsed, node.IsNull() ? node : Node.Number(double.Parse(generated, CultureInfo.InvariantCulture)));
            });

        static readonly (Type[] definitions, Type[] concretes) _types = ReflectionUtility.AllTypes
            .Where(type => type.IsPublic && !type.IsAbstract)
            .Where(type =>
                type.IsPrimitive ||
                type.IsEnum ||
                type.IsSerializable ||
                type.Namespace.Contains(nameof(Entia)) ||
                type.Namespace.Contains($"{nameof(System)}.{nameof(System.IO)}") ||
                type.Namespace.Contains($"{nameof(System)}.{nameof(System.Text)}") ||
                type.Namespace.Contains($"{nameof(System)}.{nameof(System.Linq)}") ||
                type.Namespace.Contains($"{nameof(System)}.{nameof(System.Buffers)}") ||
                type.Namespace.Contains($"{nameof(System)}.{nameof(System.Data)}") ||
                type.Namespace.Contains($"{nameof(System)}.{nameof(System.Collections)}") ||
                type.Namespace.Contains($"{nameof(System)}.{nameof(System.ComponentModel)}"))
            .Distinct()
            .Split(type => type.IsGenericTypeDefinition);
        static readonly Type[] _generics = _types.definitions
            .Repeat(10)
            .Select(definition => Option.Try(() => definition.MakeGenericType(definition.GetGenericArguments()
                .Select(_ => _types.concretes[_random.Next(_types.concretes.Length)]))))
            .Choose()
            .ToArray();
        static readonly Type[] _concretes = _types.concretes.Append(_generics);

        static readonly Generator<object> _arrays = _concretes
            .Select(type => Option.Try(() => Array.CreateInstance(type, 1)).Box())
            .Choose()
            .Select(Constant)
            .Any();
        static readonly Generator<object> _enumerable = _concretes
            .Select(type => Option.And(type.EnumerableArgument(true), type.EnumerableConstructor(true)))
            .Choose()
            .Select(pair => Option.Try(() => pair.Item2.Invoke(new[] { Array.CreateInstance(pair.Item1, 0) })))
            .Choose()
            .Select(Constant)
            .Any();
        static readonly Generator<object> _default = _concretes
            .Select(type => type.DefaultConstructor())
            .Choose()
            .Select(constructor => Option.Try(() => constructor.Invoke(Array.Empty<object>())))
            .Choose()
            .Select(Constant)
            .Any();
        static readonly Generator<object> _instance = Any(_default, _arrays, _enumerable);

        public static void Run()
        {
            _string.Check("Generate/parse symmetry for String nodes.");
            _number.Check("Generate/parse symmetry for Number nodes.");
            _node.Check("Generate/parse symmetry for Root nodes.");

            _number.Map(node =>
            {
                var generated = Serialization.Generate(node);
                var parsed = Serialization.Parse(generated);
                var system = node.IsNull() ? node : Node.Number(double.Parse(generated, CultureInfo.InvariantCulture));
                return (node, generated, parsed, system);
            }).Check("Generate/parse/double.Parse symmetry for Number nodes.", tuple =>
                tuple.node == tuple.parsed &&
                tuple.node == tuple.system &&
                tuple.parsed == tuple.system);

            _node.Map(node =>
            {
                string Generate(Node node) => Serialization.Generate(node.Map(child => Generate(child)));
                Node Parse(string json) => Serialization.Parse(json).Or(Node.Null).Map(child => Parse(child.AsString()));
                var generated = Generate(node);
                var parsed = Parse(generated);
                return (node, generated, parsed);
            }).Check("Generate/parse symmetry for nested json.", tuple => tuple.node == tuple.parsed);

            _instance.Map(value =>
            {
                var generated = Serialization.Serialize(value, _settings);
                var parsed = Serialization.Deserialize<object>(generated, _settings);
                return (value, generated, parsed);
            }).Check("Serialize/deserialize abstract instances to same type.", tuple =>
                tuple.value is object &&
                tuple.parsed.TryValue(out var value) && value is object &&
                tuple.value.GetType() == value.GetType());

            // TODO: Add test for parsing foreign jsons
            // TODO: Add test for comparing output with Json.Net and .Net Json parser.
        }

        static Failure<T>[] Check<T>(this Generator<T> generator, string name, Func<T, bool> prove) =>
            generator.Prove(name, prove).Log(name).Check();

        static Failure<(Node, string, Result<Node>)>[] Check(this Generator<Node> generator, string name) => generator
            .Map(node =>
            {
                var json = Serialization.Generate(node);
                var result = Serialization.Parse(json);
                return (node, json, result);
            })
            .Prove(name, values => values.node == values.result)
            .Log(name)
            .Check();
    }
}