/* DO NOT MODIFY: The content of this file has been generated by the script 'ConcreteTuple.csx'. */

using Entia.Serialization;

namespace Entia.Serializers
{
    public sealed class ConcreteTuple<T1, T2> : Serializer<(T1, T2)>
    {
        public readonly Serializer<T1> Item1; public readonly Serializer<T2> Item2;

        public ConcreteTuple() { }
        public ConcreteTuple(Serializer<T1> item1 = null, Serializer<T2> item2 = null)
        {
            Item1 = item1; Item2 = item2;
        }

        public override bool Serialize(in (T1, T2) instance, in SerializeContext context) =>
            context.Serialize(instance.Item1, Item1) && context.Serialize(instance.Item2, Item2);

        public override bool Instantiate(out (T1, T2) instance, in DeserializeContext context)
        {
            if (context.Deserialize(out T1 item1, Item1) && context.Deserialize(out T2 item2, Item2))
            {
                instance = (item1, item2);
                return true;
            }
            instance = default;
            return false;
        }

        public override bool Initialize(ref (T1, T2) instance, in DeserializeContext context) => true;
    }
    public sealed class ConcreteTuple<T1, T2, T3> : Serializer<(T1, T2, T3)>
    {
        public readonly Serializer<T1> Item1; public readonly Serializer<T2> Item2; public readonly Serializer<T3> Item3;

        public ConcreteTuple() { }
        public ConcreteTuple(Serializer<T1> item1 = null, Serializer<T2> item2 = null, Serializer<T3> item3 = null)
        {
            Item1 = item1; Item2 = item2; Item3 = item3;
        }

        public override bool Serialize(in (T1, T2, T3) instance, in SerializeContext context) =>
            context.Serialize(instance.Item1, Item1) && context.Serialize(instance.Item2, Item2) && context.Serialize(instance.Item3, Item3);

        public override bool Instantiate(out (T1, T2, T3) instance, in DeserializeContext context)
        {
            if (context.Deserialize(out T1 item1, Item1) && context.Deserialize(out T2 item2, Item2) && context.Deserialize(out T3 item3, Item3))
            {
                instance = (item1, item2, item3);
                return true;
            }
            instance = default;
            return false;
        }

        public override bool Initialize(ref (T1, T2, T3) instance, in DeserializeContext context) => true;
    }
    public sealed class ConcreteTuple<T1, T2, T3, T4> : Serializer<(T1, T2, T3, T4)>
    {
        public readonly Serializer<T1> Item1; public readonly Serializer<T2> Item2; public readonly Serializer<T3> Item3; public readonly Serializer<T4> Item4;

        public ConcreteTuple() { }
        public ConcreteTuple(Serializer<T1> item1 = null, Serializer<T2> item2 = null, Serializer<T3> item3 = null, Serializer<T4> item4 = null)
        {
            Item1 = item1; Item2 = item2; Item3 = item3; Item4 = item4;
        }

        public override bool Serialize(in (T1, T2, T3, T4) instance, in SerializeContext context) =>
            context.Serialize(instance.Item1, Item1) && context.Serialize(instance.Item2, Item2) && context.Serialize(instance.Item3, Item3) && context.Serialize(instance.Item4, Item4);

        public override bool Instantiate(out (T1, T2, T3, T4) instance, in DeserializeContext context)
        {
            if (context.Deserialize(out T1 item1, Item1) && context.Deserialize(out T2 item2, Item2) && context.Deserialize(out T3 item3, Item3) && context.Deserialize(out T4 item4, Item4))
            {
                instance = (item1, item2, item3, item4);
                return true;
            }
            instance = default;
            return false;
        }

        public override bool Initialize(ref (T1, T2, T3, T4) instance, in DeserializeContext context) => true;
    }
    public sealed class ConcreteTuple<T1, T2, T3, T4, T5> : Serializer<(T1, T2, T3, T4, T5)>
    {
        public readonly Serializer<T1> Item1; public readonly Serializer<T2> Item2; public readonly Serializer<T3> Item3; public readonly Serializer<T4> Item4; public readonly Serializer<T5> Item5;

        public ConcreteTuple() { }
        public ConcreteTuple(Serializer<T1> item1 = null, Serializer<T2> item2 = null, Serializer<T3> item3 = null, Serializer<T4> item4 = null, Serializer<T5> item5 = null)
        {
            Item1 = item1; Item2 = item2; Item3 = item3; Item4 = item4; Item5 = item5;
        }

        public override bool Serialize(in (T1, T2, T3, T4, T5) instance, in SerializeContext context) =>
            context.Serialize(instance.Item1, Item1) && context.Serialize(instance.Item2, Item2) && context.Serialize(instance.Item3, Item3) && context.Serialize(instance.Item4, Item4) && context.Serialize(instance.Item5, Item5);

        public override bool Instantiate(out (T1, T2, T3, T4, T5) instance, in DeserializeContext context)
        {
            if (context.Deserialize(out T1 item1, Item1) && context.Deserialize(out T2 item2, Item2) && context.Deserialize(out T3 item3, Item3) && context.Deserialize(out T4 item4, Item4) && context.Deserialize(out T5 item5, Item5))
            {
                instance = (item1, item2, item3, item4, item5);
                return true;
            }
            instance = default;
            return false;
        }

        public override bool Initialize(ref (T1, T2, T3, T4, T5) instance, in DeserializeContext context) => true;
    }
    public sealed class ConcreteTuple<T1, T2, T3, T4, T5, T6> : Serializer<(T1, T2, T3, T4, T5, T6)>
    {
        public readonly Serializer<T1> Item1; public readonly Serializer<T2> Item2; public readonly Serializer<T3> Item3; public readonly Serializer<T4> Item4; public readonly Serializer<T5> Item5; public readonly Serializer<T6> Item6;

        public ConcreteTuple() { }
        public ConcreteTuple(Serializer<T1> item1 = null, Serializer<T2> item2 = null, Serializer<T3> item3 = null, Serializer<T4> item4 = null, Serializer<T5> item5 = null, Serializer<T6> item6 = null)
        {
            Item1 = item1; Item2 = item2; Item3 = item3; Item4 = item4; Item5 = item5; Item6 = item6;
        }

        public override bool Serialize(in (T1, T2, T3, T4, T5, T6) instance, in SerializeContext context) =>
            context.Serialize(instance.Item1, Item1) && context.Serialize(instance.Item2, Item2) && context.Serialize(instance.Item3, Item3) && context.Serialize(instance.Item4, Item4) && context.Serialize(instance.Item5, Item5) && context.Serialize(instance.Item6, Item6);

        public override bool Instantiate(out (T1, T2, T3, T4, T5, T6) instance, in DeserializeContext context)
        {
            if (context.Deserialize(out T1 item1, Item1) && context.Deserialize(out T2 item2, Item2) && context.Deserialize(out T3 item3, Item3) && context.Deserialize(out T4 item4, Item4) && context.Deserialize(out T5 item5, Item5) && context.Deserialize(out T6 item6, Item6))
            {
                instance = (item1, item2, item3, item4, item5, item6);
                return true;
            }
            instance = default;
            return false;
        }

        public override bool Initialize(ref (T1, T2, T3, T4, T5, T6) instance, in DeserializeContext context) => true;
    }
    public sealed class ConcreteTuple<T1, T2, T3, T4, T5, T6, T7> : Serializer<(T1, T2, T3, T4, T5, T6, T7)>
    {
        public readonly Serializer<T1> Item1; public readonly Serializer<T2> Item2; public readonly Serializer<T3> Item3; public readonly Serializer<T4> Item4; public readonly Serializer<T5> Item5; public readonly Serializer<T6> Item6; public readonly Serializer<T7> Item7;

        public ConcreteTuple() { }
        public ConcreteTuple(Serializer<T1> item1 = null, Serializer<T2> item2 = null, Serializer<T3> item3 = null, Serializer<T4> item4 = null, Serializer<T5> item5 = null, Serializer<T6> item6 = null, Serializer<T7> item7 = null)
        {
            Item1 = item1; Item2 = item2; Item3 = item3; Item4 = item4; Item5 = item5; Item6 = item6; Item7 = item7;
        }

        public override bool Serialize(in (T1, T2, T3, T4, T5, T6, T7) instance, in SerializeContext context) =>
            context.Serialize(instance.Item1, Item1) && context.Serialize(instance.Item2, Item2) && context.Serialize(instance.Item3, Item3) && context.Serialize(instance.Item4, Item4) && context.Serialize(instance.Item5, Item5) && context.Serialize(instance.Item6, Item6) && context.Serialize(instance.Item7, Item7);

        public override bool Instantiate(out (T1, T2, T3, T4, T5, T6, T7) instance, in DeserializeContext context)
        {
            if (context.Deserialize(out T1 item1, Item1) && context.Deserialize(out T2 item2, Item2) && context.Deserialize(out T3 item3, Item3) && context.Deserialize(out T4 item4, Item4) && context.Deserialize(out T5 item5, Item5) && context.Deserialize(out T6 item6, Item6) && context.Deserialize(out T7 item7, Item7))
            {
                instance = (item1, item2, item3, item4, item5, item6, item7);
                return true;
            }
            instance = default;
            return false;
        }

        public override bool Initialize(ref (T1, T2, T3, T4, T5, T6, T7) instance, in DeserializeContext context) => true;
    }
}