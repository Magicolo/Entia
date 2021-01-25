namespace Entia.Experiment
{
    /*
    static class Boba
    {
        [Macro.CallSite(nameof(Tuple))]
        static Macro Fett(Macro.CallSite call)
        {
            var generics = call.Arguments.Select((argument, index) => Macro.Generic($"T{index}"));
            var parameters = call.Arguments.Select((argument, index) => Macro.Parameter($"value{index}", argument.Type));
            var type = Macro.Syntax.Expression.Tuple(generics);
            var body = Macro.Syntax.Expression.Arrow(Macro.Syntax.Expression.Tuple(parameters));
            // Uses 'Tuple' as a template to copy name, accessibility, modifiers, attributes.
            return Macro.Function(Macro.Signature(Tuple).With(generics, parameters, type), body);
        }

        public static ITuple Tuple(params object[] values) => throw new NotImplementedException();
    }
    */
}