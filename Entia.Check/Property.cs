namespace Entia.Check
{
    public delegate Property[] Prove<T>(T value);
    public readonly struct Prover<T>
    {
        public readonly Prove<T> Prove;
        public Prover(Prove<T> prove) { Prove = prove; }
    }

    public readonly struct Property
    {
        public static implicit operator Property((string name, bool proof) pair) => new(pair.name, pair.proof);
        public readonly string Name;
        public readonly bool Proof;
        public Property(string name, bool proof) { Name = name; Proof = proof; }
        public Property With(string? name = null, bool? proof = null) => new(name ?? Name, proof ?? Proof);
        public override string ToString() => Name;
    }
}