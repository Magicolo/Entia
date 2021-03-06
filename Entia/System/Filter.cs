using System;
using System.Linq;
using Entia.Modules.Component;

namespace Entia.Experimental
{
    public readonly struct Filter
    {
        public static Filter All(params Filter[] filters)
        {
            if (filters.Any(filter => filter.Matches == False.Matches)) return False;
            filters = filters.Where(filter => filter.Matches != True.Matches).ToArray();
            if (filters.Length == 0) return True;
            if (filters.Length == 1) return filters[0];
            return new Filter(segment =>
            {
                foreach (var filter in filters)
                {
                    if (filter.Matches(segment)) continue;
                    return false;
                }
                return true;
            });
        }

        public static Filter Any(params Filter[] filters)
        {
            if (filters.Any(filter => filter.Matches == True.Matches)) return True;
            filters = filters.Where(filter => filter.Matches != False.Matches).ToArray();
            if (filters.Length == 0) return False;
            if (filters.Length == 1) return filters[0];
            return new Filter(segment =>
            {
                foreach (var filter in filters) if (filter.Matches(segment)) return true;
                return false;
            });
        }

        public static Filter None(params Filter[] filters) => Not(Any(filters));

        public static Filter Not(Filter filter) =>
            filter.Matches == True.Matches ? False :
            filter.Matches == False.Matches ? True :
            new Filter(segment => !filter.Matches(segment));

        public static Filter Has<T>() where T : IComponent =>
            ComponentUtility.TryGetConcreteMask<T>(out var mask) ?
            new Filter(segment => segment.Mask.HasAny(mask)) : False;

        public static Filter Has(Type component) =>
            ComponentUtility.TryGetConcreteMask(component, out var mask) ?
            new Filter(segment => segment.Mask.HasAny(mask)) : False;

        public static readonly Filter True = new Filter(_ => true);
        public static readonly Filter False = new Filter(_ => false);

        public readonly Func<Segment, bool> Matches;
        public Filter(Func<Segment, bool> matches) { Matches = matches; }
    }
}