using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Entia.Check;
using Entia.Core;
using static Entia.Check.Generator;

namespace Entia.Json.Test
{
    public static class Checks
    {
        static readonly Settings _settings = Settings.Default.With(Features.All);
        static readonly Lazy<Type[]> _types = new Lazy<Type[]>(() =>
        {
            var random = new Random();
            var (definitionTypes, concreteTypes) = ReflectionUtility.AllTypes
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
            var valueTypes = concreteTypes.Where(type => type.IsValueType).ToArray();
            return ArrayUtility.Concatenate(
                concreteTypes,
                definitionTypes.SelectMany(definition => Generics(definition, concreteTypes, 10)).ToArray(),
                Generics(typeof(Nullable<>), valueTypes, 100).ToArray(),
                Generics(typeof(Option<>), concreteTypes, 100).ToArray(),
                Generics(typeof(Result<>), concreteTypes, 100).ToArray(),
                Generics(typeof(List<>), concreteTypes, 100).ToArray(),
                Generics(typeof(Dictionary<,>), concreteTypes, 100).ToArray());

            IEnumerable<Type> Generics(Type definition, Type[] types, uint count)
            {
                var parameters = definition.GetGenericArguments();
                while (count-- > 0)
                {
                    for (int i = 0; i < parameters.Length; i++)
                        parameters[i] = types[random.Next(types.Length)];
                    if (Option.Try(() => definition.MakeGenericType(parameters)).TryValue(out var generic))
                        yield return generic;
                }
            }
        });

        static readonly Generator<Enum> _enum = _types.Value
            .Where(type => type.IsEnum)
            .SelectMany(type => Enum.GetValues(type).OfType<Enum>())
            .Select(Constant)
            .Any();
        static readonly Generator<Node> _boolean = Any(
            Constant(Node.True),
            Constant(Node.False),
            Generator.Boolean.Map(Node.Boolean));
        static readonly Generator<Node> _number = Any(
            Any(Constant(Node.Zero), Constant(Node.Number(float.NaN)), Infinity.Map(Node.Number)),
            Any(_enum.Map(Node.Number), Character.Map(Node.Number)),
            Integer.Map(Node.Number),
            Rational.Map(Node.Number),
            All(Rational, Rational).Map(values => Node.Number(values[0] / values[1])));
        static readonly Generator<Node> _string = Any(
            Any(Constant(Node.EmptyString), _enum.Map(Node.String)),
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
        static readonly Generator<object> _instance = new[]{
            _types.Value.Select(type => Option.Try(() => Array.CreateInstance(type, 1)).Box()),
            _types.Value
                .Select(type => Option.And(type.EnumerableArgument(true), type.EnumerableConstructor(true)))
                .Choose()
                .Select(pair => Option.Try(() => pair.Item2.Invoke(new[] { Array.CreateInstance(pair.Item1, 0) }))),
            _types.Value
                .Select(type => type.DefaultConstructor())
                .Choose()
                .Select(constructor => Option.Try(() => constructor.Invoke(Array.Empty<object>())))
        }.Flatten().Choose().Select(Constant).Any();

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

            // BUG: Generate/Parse are not symmetric for very large or very small rational numbers.
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
            .Check(name, values => values.node == values.result);
    }
}