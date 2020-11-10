using System.Reflection;
using Entia.Json.Converters;

namespace Entia.Json
{
    public sealed class ConcreteAssembly : Converter<Assembly>
    {
        public override Node Convert(in Assembly instance, in ToContext context) => instance.GetName().Name;
        public override Assembly Instantiate(in FromContext context) =>
            context.Node.TryString(out var value) ? Assembly.Load(value) : default;
    }
}