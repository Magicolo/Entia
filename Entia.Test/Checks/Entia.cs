using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Entia.Check;
using Entia.Core;
using static Entia.Check.Generator;

namespace Entia
{
    public static class Checks
    {
        static readonly Generator<Type> _component = Types.Derived<IComponent>().Choose(Types.Make).Flatten();

        public static void Run()
        {
            var t = Types.Derived<ITuple>(Types.Filter.Concrete).Make().Sample(100).ToArray();
            var e = Types.Derived<Enum>(Types.Filter.Concrete).Sample(100).ToArray();
            var b = Types.Derived<IComponent>().Make().Sample(100).ToArray();
        }
    }
}