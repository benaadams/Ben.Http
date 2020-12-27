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
        if (context.SyntaxReceiver is not DatabaseReceiver receiver || receiver.Invocations is null) return;

        HashSet<(string dbType, string arg, string methodName)> invocations = new();

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
                    var semanticModel = context.Compilation.GetSemanticModel(invocation.SyntaxTree);
                    ITypeSymbol? type = null;
                    foreach (SyntaxNode child in expression.ChildNodes())
                    {
                        if (child is IdentifierNameSyntax identifier)
                        {
                            type = semanticModel.GetTypeInfo(identifier).Type;
                        }
                        else if (child is GenericNameSyntax methodIdent)
                        {
                            var typeArgs = methodIdent.TypeArgumentList;
                            if (type is not null && typeArgs.Arguments.Count == 1)
                            {
                                var dbType = type.ToString();
                                var arg = typeArgs.Arguments[0];
                                var argStr = arg.ToString();
                                var methodName = SanitizeIdentifier(arg.ToString());

                                if (!invocations.Contains((dbType, argStr, methodName)))
                                {
                                    GenerateQueryMethod(semanticModel, dbType, methodName, arg, sb);
                                    invocations.Add((dbType, argStr, methodName));
                                }
                            }
                            break;
                        }
                    }
                }
            }
        }

        foreach (var db in invocations.GroupBy(type => type.dbType))
        {
            sb.AppendLine(@$"
        public static Task<List<T>> QueryAsync<T>(this {db.Key} conn, string sql, bool autoClose = true)
        {{");
            foreach (var item in db)
            {
                sb.AppendLine(@$"
            if (typeof(T) == typeof({item.arg}))
            {{
                return (Task<List<T>>)(object)QueryAsync_{item.methodName}(conn, sql);
            }}");

            }
            sb.AppendLine(@$"
            throw new NotImplementedException();
        }}");
        }

        sb.AppendLine(@"
    }
}");
        context.AddSource("Database", sb.ToString());
    }

    private void GenerateQueryMethod(SemanticModel semanticModel, string dbType, string methodName, TypeSyntax arg, StringBuilder sb)
    {
        sb.AppendLine(@$"
        private static async Task<List<{arg}>> QueryAsync_{methodName}({dbType} conn, string sql, bool autoClose = true)
        {{
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            var list = new List<{arg}>(16);
            await conn.OpenAsync();

            using var reader = await cmd.ExecuteReaderAsync();");

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

            if (autoClose) conn.Close();

            return list;      
        }}");
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
