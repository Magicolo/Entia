using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Entia.Generate
{
    [Generator]
    public class System : ISourceGenerator
    {
        const string Babylon2 = nameof(Babylon2);

        sealed class Receiver : ISyntaxReceiver
        {
            public readonly List<InvocationExpressionSyntax> Invocations = new();

            public void OnVisitSyntaxNode(SyntaxNode node)
            {
                if (node is InvocationExpressionSyntax
                    {
                        Expression:
                            MemberAccessExpressionSyntax { 
                                Expression: IdentifierNameSyntax { Identifier:{ValueText: Babylon2 } }, 
                                Name: { Identifier: { ValueText: "Run" } } },
                        ArgumentList: { Arguments: { Count: 1 } }
                    } invocation)
                    Invocations.Add(invocation);
            }
        }

        public void Initialize(GeneratorInitializationContext context) =>
            context.RegisterForSyntaxNotifications(() => new Receiver());

        public void Execute(GeneratorExecutionContext context)
        {
            if (context.SyntaxReceiver is not Receiver receiver) return;

            context.AddSource($"{nameof(System)}.Template.cs",
$@"public static class {Babylon2}
{{
    public static void Run(System.Delegate  run) {{ }}
{string.Join(Environment.NewLine, Body())}
}}");
            IEnumerable<string> Body()
            {
                var count = 0;
                var set = new HashSet<string>();
                foreach (var invocation in receiver.Invocations)
                {
                    var model = context.Compilation.GetSemanticModel(invocation.SyntaxTree);
                    if (invocation.ArgumentList.Arguments[0].Expression is ParenthesizedLambdaExpressionSyntax lambda &&
                        lambda.ParameterList.Parameters.Select(parameter => $"{string.Join("", parameter.Modifiers.Select(modifier => $"{modifier} "))}{model.GetTypeInfo(parameter.Type).Type} {parameter.Identifier}") is var parameters &&
                        $"{string.Join(", ", parameters)}" is var display &&
                        set.Add(display))
                    {
                        var name = $"Run{count++}";
                        yield return $"public delegate void {name}({display});";
                        yield return $"public static void Run({name} run) {{ }}";
                    }
                    else if (
                        model.GetTypeInfo(invocation.ArgumentList.Arguments[0].Expression).Type?.ToString() is string argument &&
                        set.Add(argument))
                        yield return $"public static void Run({argument} run) {{ }}";
                    else
                        context.ReportDiagnostic(Diagnostic.Create(new("A", "B", $"I: {invocation}", "D", DiagnosticSeverity.Warning, true), invocation.GetLocation()));
                }
            }
        }
    }
}
