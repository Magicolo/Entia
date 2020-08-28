using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using Entia.Core;
using Entia.Core.Providers;

namespace Entia.Json.Converters
{
    namespace Providers
    {
        public sealed class Serializable : Provider<IConverter>
        {
            public override IEnumerable<IConverter> Provide(TypeData type)
            {
                if (type.Interfaces.Any(@interface => @interface == typeof(ISerializable)) &&
                    type.InstanceConstructors.TryFirst(constructor =>
                        constructor.Parameters.Length == 2 &&
                        constructor.Parameters[0].ParameterType == typeof(SerializationInfo) &&
                        constructor.Parameters[1].ParameterType == typeof(StreamingContext), out var constructor))
                    yield return new AbstractSerializable(constructor);
            }
        }
    }

    public sealed class AbstractSerializable : Converter<ISerializable>
    {
        static readonly FormatterConverter _converter = new FormatterConverter();
        static readonly StreamingContext _context = new StreamingContext(StreamingContextStates.All);

        readonly ConstructorInfo _constructor;

        public AbstractSerializable(ConstructorInfo constructor) { _constructor = constructor; }

        public override Node Convert(in ISerializable instance, in ConvertToContext context)
        {
            var info = new SerializationInfo(context.Type, _converter);
            instance.GetObjectData(info, _context);
            var children = new Node[info.MemberCount * 2];
            var index = 0;
            foreach (var pair in info)
            {
                children[index++] = pair.Name;
                children[index++] = context.Convert(pair.Value);
            }
            return Node.Object(children);
        }

        public override ISerializable Instantiate(in ConvertFromContext context) =>
            FormatterServices.GetUninitializedObject(context.Type) as ISerializable;

        public override void Initialize(ref ISerializable instance, in ConvertFromContext context)
        {
            var info = new SerializationInfo(context.Type, _converter);
            var children = context.Node.Children;
            for (int i = 1; i < children.Length; i += 2)
            {
                if (children[i - 1].TryString(out var key))
                    info.AddValue(key, context.Convert<object>(children[i]));
            }
            _constructor.Invoke(instance, new object[] { info, _context });
            if (instance is IDeserializationCallback callback) callback.OnDeserialization(this);
        }
    }
}