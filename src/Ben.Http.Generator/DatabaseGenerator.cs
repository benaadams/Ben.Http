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
        if (context.SyntaxReceiver is not DatabaseReceiver receiver || 
            (receiver.QueryAsyncInvocations is null && 
             receiver.QueryRowAsyncInvocations is null)) return;


        StringBuilder sb = new StringBuilder();
        sb.AppendLine($@"// Source Generated at {DateTimeOffset.Now:R}
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Ben.Http
{{
    public static class DatabaseExtensionsGenerated
    {{");

        if (receiver.QueryAsyncInvocations is not null)
        {
            OutputQueryAsync(context.Compilation, receiver.QueryAsyncInvocations, sb);
        }
        if (receiver.QueryRowAsyncInvocations is not null)
        {
            OutputQueryRowAsync(context.Compilation, receiver.QueryRowAsyncInvocations, sb);
        }

        sb.AppendLine(@"
    }
}");
        context.AddSource("Database", sb.ToString());
    }

    private void OutputQueryRowAsync(Compilation compilation, List<InvocationExpressionSyntax> queryAsyncInvocations, StringBuilder sb)
    {
        HashSet<(string dbType, string arg0, string arg1, string methodName)> invocations = new();
        foreach (var invocation in queryAsyncInvocations)
        {
            var arguments = invocation.ArgumentList.Arguments;
            if (arguments.Count != 2) continue;
            var argument = arguments[0].Expression;
            if (argument.Kind() != SyntaxKind.StringLiteralExpression)
            {
                continue;
            }
            argument = arguments[1].Expression;

            var expression = invocation.Expression;
            if (expression is MemberAccessExpressionSyntax member)
            {
                if (member.IsKind(SyntaxKind.SimpleMemberAccessExpression))
                {
                    var semanticModel = compilation.GetSemanticModel(invocation.SyntaxTree);
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
                            if (type is not null && typeArgs.Arguments.Count == 2)
                            {
                                var dbType = type.ToString();
                                var arg0 = typeArgs.Arguments[0];
                                var arg1 = typeArgs.Arguments[1];
                                var argStr0 = arg0.ToString();
                                var argStr1 = arg1.ToString();
                                var methodName = SanitizeIdentifier(argStr0 + argStr1);

                                if (!invocations.Contains((dbType, argStr0, argStr1, methodName)))
                                {
                                    GenerateQueryRowMethod(semanticModel, dbType, methodName, arg0, arg1, sb);
                                    invocations.Add((dbType, argStr0, argStr1, methodName));
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
        public static Task<TResult> QueryRowAsync<TResult, TValue>(this {db.Key} conn, string sql, (string name, TValue value) parameter, bool autoClose = true)
        {{");
            foreach (var item in db)
            {
                sb.AppendLine(@$"
            if (typeof(TResult) == typeof({item.arg0}) && typeof(TValue) == typeof({item.arg1}))
            {{
                return (Task<TResult>)(object)QueryRowAsync_{item.methodName}(conn, sql, (parameter.name, ({item.arg1})(object)parameter.value!), autoClose);
            }}");

            }
            sb.AppendLine(@$"
            throw new NotImplementedException();
        }}");
        }
    }

    private void GenerateQueryRowMethod(SemanticModel semanticModel, string dbType, string methodName, TypeSyntax argResult, TypeSyntax argParam, StringBuilder sb)
    {
        var dbPrefix = dbType.Replace("Connection", "");
        sb.AppendLine(@$"
        private static async Task<{argResult}> QueryRowAsync_{methodName}({dbType} conn, string sql, (string name, {argParam} value) param, bool autoClose)
        {{
            using var cmd = new {dbPrefix}Command(sql, conn);
            var parameter = new {dbPrefix}Parameter<{argParam}>(parameterName: param.name, value: param.value);
            cmd.Parameters.Add(parameter);
            await conn.OpenAsync();

            using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();");

        var type = semanticModel.GetTypeInfo(argResult).Type;

        var fields = type.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsImplicitlyDeclared).ToArray();
        var properties = type.GetMembers().OfType<IPropertySymbol>().Where(f => !f.IsReadOnly).ToArray();

        sb.Append(@$"
            if (!autoClose)
            {{
                return new ()
                {{");
        foreach (var field in fields)
        {
            sb.Append(@$"
                    {field.Name} = reader.Get{GetType(field.Type.SpecialType)}(""{field.Name}""),");
        }
        foreach (var property in properties)
        {
            sb.Append(@$"
                    {property.Name} = reader.Get{GetType(property.Type.SpecialType)}(""{property.Name}""),");
        }

        sb.Remove(sb.Length - 1, 1);

        sb.Append(@$"
                }};
            }}
            else
            {{
                var retVal = new {argResult}()
                {{");
        foreach (var field in fields)
        {
            sb.Append(@$"
                    {field.Name} = reader.Get{GetType(field.Type.SpecialType)}(""{field.Name}""),");
        }
        foreach (var property in properties)
        {
            sb.Append(@$"
                    {property.Name} = reader.Get{GetType(property.Type.SpecialType)}(""{property.Name}""),");
        }

        sb.Remove(sb.Length - 1, 1);

        sb.AppendLine(@$"
                }};
            
                conn.Close();

                return retVal;
            }}
    
        }}");
    }


    private void OutputQueryAsync(Compilation compilation, List<InvocationExpressionSyntax> queryAsyncInvocations, StringBuilder sb)
    {
        HashSet<(string dbType, string arg, string methodName)> invocations = new();
        foreach (var invocation in queryAsyncInvocations)
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
                    var semanticModel = compilation.GetSemanticModel(invocation.SyntaxTree);
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
                                var methodName = SanitizeIdentifier(argStr);

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
        public static Task<List<TResult>> QueryAsync<TResult>(this {db.Key} conn, string sql, bool autoClose = true)
        {{");
            foreach (var item in db)
            {
                sb.AppendLine(@$"
            if (typeof(TResult) == typeof({item.arg}))
            {{
                return (Task<List<TResult>>)(object)QueryAsync_{item.methodName}(conn, sql, autoClose);
            }}");

            }
            sb.AppendLine(@$"
            throw new NotImplementedException();
        }}");
        }
    }

    private void GenerateQueryMethod(SemanticModel semanticModel, string dbType, string methodName, TypeSyntax arg, StringBuilder sb)
    {
        sb.AppendLine(@$"
        private static async Task<List<{arg}>> QueryAsync_{methodName}({dbType} conn, string sql, bool autoClose)
        {{
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            var list = new List<{arg}>(16);
            await conn.OpenAsync();

            using var reader = await cmd.ExecuteReaderAsync();");

        var type = semanticModel.GetTypeInfo(arg).Type;

        var count = 0;
        var fields = type.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsImplicitlyDeclared).ToArray();
        var properties = type.GetMembers().OfType<IPropertySymbol>().Where(f => !f.IsReadOnly).ToArray();

        foreach (var field in fields)
        {
            sb.Append(@$"
            var f{count} = reader.GetOrdinal(""{field.Name}"");");

            count++;
        }
        foreach (var propery in properties)
        {
            sb.Append(@$"
            var f{count} = reader.GetOrdinal(""{propery.Name}"");");

            count++;
        }
        count = 0;

        sb.Append(@$"
            while (await reader.ReadAsync())
            {{
                list.Add(new ()
                {{");
        foreach (var field in fields)
        {
            sb.Append(@$"
                    {field.Name} = reader.Get{GetType(field.Type.SpecialType)}(f{count}),");

            count++;
        }
        foreach (var property in properties)
        {
            sb.Append(@$"
                    {property.Name} = reader.Get{GetType(property.Type.SpecialType)}(f{count}),");

            count++;
        }

        sb.Remove(sb.Length - 1, 1);

        sb.AppendLine(@$"
                }});
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
        public List<InvocationExpressionSyntax>? QueryAsyncInvocations { get; private set; }
        public List<InvocationExpressionSyntax>? QueryRowAsyncInvocations { get; private set; }

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
                                switch (valueText)
                                {
                                    case "QueryAsync":
                                        (QueryAsyncInvocations ??= new()).Add(invocation);
                                        break;
                                    case "QueryRowAsync":
                                        (QueryRowAsyncInvocations ??= new()).Add(invocation);
                                        break;
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
