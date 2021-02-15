using Entia.Core;

namespace Entia.Check
{
    public readonly struct Outcome<T>
    {
        public readonly T Value;
        public readonly Mutation<T>[] Mutations;
        public readonly Property[] Properties;

        public Outcome(T value, Mutation<T>[] mutations, Property[] properties)
        {
            Value = value;
            Mutations = mutations;
            Properties = properties;
        }

        public override string ToString() => string.Join(", ", Mutations);
    }

    public delegate Property[] Mutate<T>(T value);
    public readonly struct Mutation<T>
    {
        public readonly string Name;
        public readonly Mutate<T> Mutate;
        public Mutation(string name, Mutate<T> mutate) { Name = name; Mutate = mutate; }
        public override string ToString() => Name;
    }

    public static class Mutation
    {
        public static Mutation<T> From<T>(string name, Mutate<T> mutate) => new(name, mutate);
        public static Mutation<T> From<T>(Mutate<T> mutate) => From(mutate.Method.Format(), mutate);

        public static Outcome<object?> Box<T>(this Outcome<T> outcome) =>
            new(outcome.Value, outcome.Mutations.Select(mutation => mutation.Box()), outcome.Properties);
        public static Mutation<object?> Box<T>(this Mutation<T> mutation) =>
            new(mutation.Name, value => mutation.Mutate((T)value!));
        public static Outcome<T> Add<T>(this Outcome<T> outcome, params Property[] properties) =>
            new(outcome.Value, outcome.Mutations, outcome.Properties.Append(properties));
    }
}