using System;
using System.Linq;
using System.Runtime.CompilerServices;
using static Entia.Check.Generator;
using Entia.Core;

namespace Entia.Check
{
    public static class Checks
    {
        public static void Run()
        {
            Zero.Check("Zero == 0.", value => value == 0);
            One.Check("One == 1.", value => value == 1);
            Letter.Check("Letter is letter.", char.IsLetter);
            Digit.Check("Digit is digit.", char.IsDigit);
            ASCII.Check("ASCII is ascii.", value => value < 128);
            Letter.String(Range(100)).Check("String(Letter) is letter.", value => value.Length <= 100 && value.All(char.IsLetter));
            Digit.String(Range(100)).Check("String(Digit) is digit.", value => value.Length <= 100 && value.All(char.IsDigit));
            ASCII.String(Range(100)).Check("String(ASCII) is ascii.", value => value.Length <= 100 && value.All(value => value < 128));
            Infinity.Check("Infinity 'float' generator.", float.IsInfinity);
            String(Range(100)).Bind(value => Constant(value).Map(constant => (value, constant))).Check("Constant is constant.", pair => pair.value == pair.constant);
            Enumeration().Check("Enumeration is enum.", value => value is Enum);

            All(Zero).Check("All(1) produces arrays of length 1.", values => values.Length == 1 && values.All(value => value == 0));
            All(Zero, Zero).Check("All(2) produces arrays of length 2.", values => values.Length == 2 && values.All(value => value == 0));
            All(Zero, Zero, Zero).Check("All(3) produces arrays of length 3.", values => values.Length == 3 && values.All(value => value == 0));
            Zero.Map(Constant).Repeat(Range(100)).Bind(constants => All(constants).Map(values => (values, constants)))
                .Check("All(x) produces arrays of length x.", pair => pair.values.Length == pair.constants.Length);
            Any(Zero, One).Check("Any(Zero, One) chooses from its inputs.", value => value == 0 || value == 1);
            Any(0, 1).Repeat(100).Check("Any(0, 1).Repeat produces both values.", values => values.Contains(0) && values.Contains(1));
            Range(-10, 10).Check("Range(-10, 10) is in range.", value => value >= -10 && value <= 10);
            Range('a', 'z').Check("Range('a', 'z') is in range.", value => value >= 'a' && value <= 'z');
            Range(-1f, 1f).Check("Range(-1f, 1f) is in range.", value => value >= -1f && value <= 1f);
            Zero.Repeat(Range(100)).Check("Repeat(100) is in range.", values => values.Length <= 100);
            Types.Enumeration.Bind(type => Enumeration(type).Map(value => (type, value)))
                .Check("Enumeration(type) produces values of the same type.", pair => pair.type == pair.value.GetType());
            Integer.Filter(value => value % 2 == 0).Check("Filter filters for even numbers.", value => value % 2 == 0);
            Rational.Filter(value => value >= 0f).Check("Filter filters for positive numbers.", value => value >= 0f);

            Types.Abstract.Check("Types.Abstract is abstract.", type => type.IsAbstract);
            Types.Interface.Check("Types.Interface is interface.", type => type.IsInterface);
            Types.Reference.Check("Types.Reference is class.", type => type.IsClass);
            Types.Value.Check("Types.Value is value type.", type => type.IsValueType);
            Types.Array.Check("Types.Array is array.", type => type.IsArray);
            Types.Primitive.Check("Types.Primitive is primitive.", type => type.IsPrimitive);
            Types.Enumeration.Check("Types.Enumeration is enum.", type => type.IsEnum);
            Types.Flags.Check("Types.Flags is flags.", type => type.IsEnum && type.IsDefined(typeof(FlagsAttribute), true));
            Types.Definition.Check("Types.Definition is generic type definition.", type => type.IsGenericTypeDefinition);
            Types.Default.Check("Types.Generic has default constructor.", type => type.DefaultConstructor().IsSome());
            Types.Tuple.Check("Types.Tuple is 'ITuple'.", type => type.Is<ITuple>());
            Types.Generic.Check("Types.Generic is constructed generic type.", type => type.IsConstructedGenericType);
        }

        static Failure<T>[] Check<T>(this Generator<T> generator, string name, Func<T, bool> prove) =>
            generator.Prove(name, prove).Log(name).Check();
    }
}