using System.Collections.Generic;

const string phase = "TPhase";
const string message = "TMessage";

IEnumerable<(string declaration, string run1, string run2)> Generate(int depth)
{
    static IEnumerable<string> GenericParameters(int count)
    {
        if (count == 1) yield return "T";
        else for (var i = 1; i <= count; i++) yield return $"T{i}";
    }

    static string Declaration(bool hasMessage, bool hasPhase, IEnumerable<string> generics, IEnumerable<string> constraints)
    {
        var suffix = "";
        if (hasPhase) suffix += "P";
        if (hasMessage) suffix += "M";
        var parameters = generics.Select((generic, index) => $"ref {generic} resource{index + 1}");
        if (hasMessage && hasPhase)
        {
            generics = generics.Prepend($"{message}").Prepend($"{phase}");
            parameters = parameters.Prepend($"in {message} message").Prepend($"in {phase} phase");
            constraints = constraints.Prepend($"where {message} : struct, IMessage").Prepend($"where {phase} : struct, IMessage");
        }
        else if (hasMessage || hasPhase)
        {
            generics = generics.Prepend(message);
            parameters = parameters.Prepend($"in {message} message");
            constraints = constraints.Prepend($"where {message} : struct, IMessage");
        }
        return $"public delegate void Run{suffix}<{string.Join(", ", generics)}>({string.Join(", ", parameters)}) {string.Join(" ", constraints)};";
    }

    static string Body1(bool hasPhase, IEnumerable<string> generics, IEnumerable<string> constraints)
    {
        var suffix = hasPhase ? "P" : "";
        var arguments = hasPhase ? generics.Prepend(phase) : generics;
        var resourceVars = generics.Select((generic, index) => $"Resource<{generic}> resource{index + 1}");
        var resourceRefs = generics.Select((generic, index) => $"ref resource{index + 1}.Value");
        if (hasPhase) resourceRefs = resourceRefs.Prepend("phase");
        return
$@"public static Node Run<{string.Join(", ", generics)}>(Run{suffix}<{string.Join(", ", arguments)}> run) {string.Join(" ", constraints)} =>
    Inject(({string.Join(", ", resourceVars)}) => Run((in {phase} phase) => run({string.Join(", ", resourceRefs)})));";
    }

    static string Body2(bool hasMessage, bool hasPhase, IEnumerable<string> generics, IEnumerable<string> constraints)
    {
        var suffix = "";
        if (hasPhase) suffix += "P";
        if (hasMessage) suffix += "M";
        var arguments = generics;
        if (hasMessage) arguments = arguments.Prepend(message);
        if (hasPhase) arguments = arguments.Prepend(phase);
        var resourceVars = generics.Select((generic, index) => $"Resource<{generic}> resource{index + 1}");
        var resourceRefs = generics.Select((generic, index) => $"ref resource{index + 1}.Value");
        if (hasMessage) resourceRefs = resourceRefs.Prepend("message");
        if (hasPhase) resourceRefs = resourceRefs.Prepend("phase");
        return
$@"public static Node Run<{string.Join(", ", generics)}>(Run{suffix}<{string.Join(", ", arguments)}> run) {string.Join(" ", constraints)} =>
    Inject(({string.Join(", ", resourceVars)}) => Run((in {phase} phase, in {message} message) => run({string.Join(", ", resourceRefs)})));";
    }

    for (int i = 1; i <= depth; i++)
    {
        var generics = GenericParameters(i).ToArray();
        var constraints = generics.Select((generic, index) => $"where {generic} : struct, IResource");
        var parameters = generics.Select((generic, index) => $"ref resource{index + 1}");

        yield return (
            Declaration(false, false, generics, constraints),
            Body1(false, generics, constraints),
            Body2(false, false, generics, constraints));
        yield return (
            Declaration(false, true, generics, constraints),
            Body1(true, generics, constraints),
            "");
        yield return (
            Declaration(true, false, generics, constraints),
            "",
            Body2(true, false, generics, constraints));
        yield return (
            Declaration(true, true, generics, constraints),
            "",
            Body2(true, true, generics, constraints));
    }
}

var results = Generate(9).ToArray();
var file = "Node.Run";
var code =
$@"/* DO NOT MODIFY: The content of this file has been generated by the script '{file}.csx'. */

using Entia.Injectables;
using Entia.Experimental.Systems;

namespace Entia.Experimental
{{
    namespace Systems
    {{
{string.Join(Environment.NewLine, results.Select(result => result.declaration).Where(value => !string.IsNullOrEmpty(value)))}
    }}

    public sealed partial class Node
    {{
        public static partial class Schedule<{phase}>
        {{
            public static partial class Receive<{message}>
            {{
{string.Join(Environment.NewLine, results.Select(result => result.run2).Where(value => !string.IsNullOrEmpty(value)))}
            }}

{string.Join(Environment.NewLine, results.Select(result => result.run1).Where(value => !string.IsNullOrEmpty(value)))}
        }}

    }}
}}";


File.WriteAllText($"./{file}.cs", code);