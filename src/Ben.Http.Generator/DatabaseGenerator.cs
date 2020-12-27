using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

[Generator]
public class DatabaseGenerator : ISourceGenerator
{
    public void Execute(GeneratorExecutionContext context)
    {
        var receiver = context.SyntaxReceiver as DatabaseReceiver;
        if (receiver is null || receiver.Invocations is null) return;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($@"// Source Generated at {DateTimeOffset.Now:R}
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;

namespace Ben.Http
{{
    public static class DatabaseExtensionsGenerated
    {{");
        foreach (var invocation in receiver.Invocations)
        {
            var arguments = invocation.ArgumentList.Arguments;
            if (arguments.Count != 1) continue;
            var argument = arguments[0].Expression;
            if (argument.Kind() != SyntaxKind.StringLiteralExpression)
            {
                continue;
            }

            var expression = invocation.Expression;
            if (expression is MemberAccessExpressionSyntax member)
            {
                if (member.IsKind(SyntaxKind.SimpleMemberAccessExpression))
                {
                    foreach (SyntaxNode child in expression.ChildNodes())
                    {
                        if (child is GenericNameSyntax methodIdent)
                        {
                            var typeArgs = methodIdent.TypeArgumentList;
                            if (typeArgs.Arguments.Count == 1)
                            {
                                GenerateQueryMethod(context, typeArgs.Arguments[0], sb);
                            }
                            break;
                        }
                    }
                }
            }
        }
        sb.AppendLine(@"
    }
}");
        context.AddSource("Database", sb.ToString());
    }

    private void GenerateQueryMethod(GeneratorExecutionContext context, TypeSyntax arg, StringBuilder sb)
    {
        var methodName = SanitizeIdentifier(arg.ToString());

        sb.AppendLine(@$"
        public static Task<List<{arg}>> QueryAsync<T>(this DbConnection conn, string sql)
        {{
            if (typeof(T) == typeof({arg}))
            {{
                return QueryAsync_{methodName}(conn, sql);
            }}

            throw new NotImplementedException();
        }}

        private static async Task<List<{arg}>> QueryAsync_{methodName}(DbConnection conn, string sql)
        {{
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            var list = new List<{arg}>(16);
            await conn.OpenAsync();

            var reader = await cmd.ExecuteReaderAsync();");

        var semanticModel = context.Compilation.GetSemanticModel(arg.SyntaxTree);
        var type = semanticModel.GetTypeInfo(arg).Type;

        var count = 0;
        var fields = type.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsImplicitlyDeclared).ToArray();

        foreach (var field in fields)
        {
            sb.Append(@$"
            var f{count} = reader.GetOrdinal(""{field.Name}"");");

            count++;
        }
        count = 0;

        sb.Append(@$"
            while (await reader.ReadAsync())
            {{
                list.Add((");
        foreach (var field in fields)
        {
            sb.Append(@$"
                    reader.Get{GetType(field.Type.SpecialType)}(f{count}),");

            count++;
        }

        sb.Remove(sb.Length - 1, 1);

        sb.AppendLine(@$"
                ));
            }}

            return list;      
        }}
");
    }

    private string GetType(SpecialType specialType)
    {
        var type = specialType.ToString();
        return type.Replace("System_", "");
    }

    private static string SanitizeIdentifier(string symbolName)
    {
        if (string.IsNullOrWhiteSpace(symbolName)) return string.Empty;

        var sb = new StringBuilder(symbolName.Length);
        if (!char.IsLetter(symbolName[0]))
        {
            // Must start with a letter or an underscore
            sb.Append('_');
        }

        var capitalize = true;
        foreach (var ch in symbolName)
        {
            if (!char.IsLetterOrDigit(ch))
            {
                capitalize = true;
                continue;
            }

            sb.Append(capitalize ? char.ToUpper(ch) : ch);
            capitalize = false;
        }

        return sb.ToString();
    }

    public void Initialize(GeneratorInitializationContext context)
        => context.RegisterForSyntaxNotifications(() => new DatabaseReceiver());

    class DatabaseReceiver : ISyntaxReceiver
    {
        public List<InvocationExpressionSyntax>? Invocations { get; private set; }

        public void OnVisitSyntaxNode(SyntaxNode node)
        {
            if (node.IsKind(SyntaxKind.InvocationExpression) &&
                node is InvocationExpressionSyntax invocation)
            {
                var expression = invocation.Expression;
                if (expression is MemberAccessExpressionSyntax member)
                {
                    if (member.IsKind(SyntaxKind.SimpleMemberAccessExpression))
                    {
                        foreach (SyntaxNode child in expression.ChildNodes())
                        {
                            if (child is GenericNameSyntax methodIdent)
                            {
                                var valueText = methodIdent.Identifier.ValueText;
                                if (valueText == "QueryAsync")
                                {
                                    (Invocations ??= new()).Add(invocation);
                                }
                                break;
                            }
                        }
                    }
                }
            }
        }
    }
}
