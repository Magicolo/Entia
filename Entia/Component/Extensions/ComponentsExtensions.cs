using Entia.Core.Documentation;

namespace Entia.Modules.Component
{
    public static class ComponentsExtensions
    {
        [ThreadSafe]
        public static bool HasAll(this States state, States other) => (state & other) == other;
        [ThreadSafe]
        public static bool HasAny(this States state, States other) => (state & other) != 0;
        [ThreadSafe]
        public static bool HasNone(this States state, States other) => (state & other) == 0;
    }
}