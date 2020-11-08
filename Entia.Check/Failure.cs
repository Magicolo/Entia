namespace Entia.Check
{
    public sealed class Failure<T>
    {
        public readonly T Original;
        public readonly T Shrinked;
        public readonly Property<T> Property;
        public readonly int Iteration;
        public readonly int Seed;
        public readonly double Size;

        public Failure(T original, T shrinked, Property<T> property, int iteration, int seed, double size)
        {
            Original = original;
            Shrinked = shrinked;
            Property = property;
            Iteration = iteration;
            Seed = seed;
            Size = size;
        }
    }
}