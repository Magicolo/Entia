using Entia.Core;
using Entia.Resolvables;

namespace Entia.Resolvers
{
    public interface IResolver : ITrait
    {
        bool Resolve(IResolvable resolvable);
    }

    public abstract class Resolver<T> : IResolver where T : struct, IResolvable
    {
        public abstract bool Resolve(in T resolvable);
        bool IResolver.Resolve(IResolvable resolvable) => resolvable is T casted && Resolve(casted);
    }
}