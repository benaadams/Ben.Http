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
public class MustacheGenerator : ISourceGenerator
{
    public void Execute(GeneratorExecutionContext context)
    {
        var receiver = context.SyntaxReceiver as MustacheRenderReceiver;
        if (receiver is null) return;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($@"// Source Generated at {DateTimeOffset.Now:R}
using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Ben.Http.Templates;

namespace Ben.Http
{{
    public class MustacheTemplates
    {{
        // When passing a `new byte[] {{ ... }}` to a `ReadOnlySpan<byte>` receiver,
        // the C# compiler will emit a load of the data section rather than constructing a new array.
        // We make use of that here.");

        foreach (var file in context.AdditionalFiles)
        {
            var isMustacheTemplate = string.Equals(
                context.AnalyzerConfigOptions.GetOptions(file).TryGetAdditionalFileMetadataValue("IsMustacheTemplate"),
                "true",
                StringComparison.OrdinalIgnoreCase
            );

            if (!isMustacheTemplate)
                continue;

            var content = file.GetText(context.CancellationToken)?.ToString();

            if (string.IsNullOrWhiteSpace(content))
                continue;

            ProcessFile(context, file.Path, content!, receiver, sb);

        }

        sb.AppendLine(@"
    }
}");
        context.AddSource("MustacheTemplates", sb.ToString());
    }

    const int SpacesPerIndent = 4;
    private void ProcessFile(in GeneratorExecutionContext context, string filePath, string content, MustacheRenderReceiver? receiver, StringBuilder builder)
    {
        // Generate class name from file name
        var templateName = SanitizeIdentifier(Path.GetFileNameWithoutExtension(filePath));

        // Always output non-specific writer
        builder.AppendLine(@$"
        public static void Render{templateName}<T>(T model, IBufferWriter<byte> writer)
        {{
            // Emitted as an initial call site for the template,
            // when actually called a specific call site for the exact model will be additionally be emitted.
            throw new NotImplementedException();        
        }}
");

        List<InvocationExpressionSyntax>? invocations = null;
        if (receiver?.Invocations?.TryGetValue(templateName, out invocations) ?? false)
        {
            Debug.Assert(invocations != null);

            foreach (var invocation in invocations!)
            {
                var arguments = invocation.ArgumentList.Arguments;
                if (arguments.Count != 2) continue;

                var semanticModel = context.Compilation.GetSemanticModel(invocation.SyntaxTree);
                var modelType = semanticModel.GetTypeInfo(arguments[0].Expression).Type;

                var indentSpaces = new string(' ', SpacesPerIndent * 2);

                builder.AppendLine(@$"{indentSpaces}public static void Render{templateName}({modelType} model, PipeWriter writer)");
                builder.AppendLine(@$"{indentSpaces}{{");

                indentSpaces = IncreasedIndent(indentSpaces);

                if (modelType?.ToString().StartsWith("System.Collections.Generic.List<") ?? false)
                {
                    builder.AppendLine(@$"{indentSpaces}var input = CollectionsMarshal.AsSpan(model);");
                }
                else
                {
                    builder.AppendLine(@$"{indentSpaces}var input = model;");
                }
                builder.AppendLine(@$"{indentSpaces}var output = new BufferWriter<PipeWriter>(writer, sizeHint: 1600);");

                var remaining = RenderSubSection(content.AsSpan(), "input", modelType, default, indentSpaces, builder);

                builder.Append(@$"{indentSpaces}/*{Environment.NewLine}{indentSpaces}{remaining.ToString().Replace("\n", $"\n{indentSpaces}")}{Environment.NewLine}{indentSpaces}*/{Environment.NewLine}");
                builder.Append(@$"{indentSpaces}output.Write(");
                AddUtf8ByteArray(remaining, IncreasedIndent(indentSpaces), builder);
                builder.AppendLine(");");

                builder.AppendLine(@$"{indentSpaces}output.Commit();
        }}");
            }
        }
    }

    private static ReadOnlySpan<char> RenderSubSection(ReadOnlySpan<char> input, string name, ITypeSymbol type, ReadOnlySpan<char> sectionTag, string indentSpaces, StringBuilder builder)
    {
        for (var index = input.IndexOf("{{".AsSpan(), StringComparison.Ordinal); index >= 0; index = input.IndexOf("{{".AsSpan(), StringComparison.Ordinal))
        {
            var tag = input.Slice(index + 2);
            var end = tag.IndexOf("}}".AsSpan());
            if (end <= 0)
            {
                break;
            }
            tag = tag.Slice(0, end);

            if (index > 0)
            {
                var html = input.Slice(0, index);
                builder.Append(@$"{indentSpaces}/*{Environment.NewLine}{indentSpaces}{html.ToString().Replace("\n", $"\n{indentSpaces}")}{Environment.NewLine}{indentSpaces}*/{Environment.NewLine}");

                builder.Append(@$"{indentSpaces}output.Write(");
                AddUtf8ByteArray(html, IncreasedIndent(indentSpaces), builder);
                builder.AppendLine(");");
            }

            input = input.Slice(index + end + 4);
            if (tag[0] == '#')
            {
                tag = tag.Slice(1);
                ITypeSymbol? subType = type;
                if (tag.Length == 1 && tag[0] == '.')
                {
                    tag = name.AsSpan();
                }
                else
                {
                    var tagName = tag.ToString();
                    var symbol = type.GetMembers().OfType<IFieldSymbol>().Where(f => f.Name == tagName).FirstOrDefault()?.Type;
                    if (symbol is null)
                    {
                        symbol = type.GetMembers().OfType<IPropertySymbol>().Where(p => (p.GetMethod?.Name) == "get_" + tagName).FirstOrDefault()?.Type;
                    }

                    subType = symbol;

                    if (subType is null)
                    {
                        // Skip section
                        var sectionEndTag = "{{/" + tag.ToString() + "}}";
                        var sectionEnd = input.IndexOf(sectionEndTag.AsSpan(), StringComparison.Ordinal);
                        if (end >= 0)
                        {
                            input = input.Slice(sectionEnd + sectionEndTag.Length);
                        }
                        continue;
                    }
                }

                var isEnumerable = false;
                if (subType.SpecialType == SpecialType.None)
                {
                    foreach (var iface in subType.AllInterfaces)
                    {
                        if (iface.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
                        {
                            isEnumerable = true;
                            subType = iface.TypeArguments[0];
                            break;
                        }
                    }
                }

                // section start
                builder.AppendLine($"{indentSpaces}// Start Section: {tag.ToString()}");

                if (isEnumerable)
                {
                    builder.AppendLine($"{indentSpaces}foreach (var item in {tag.ToString()})");
                    builder.AppendLine($"{indentSpaces}{{");

                    input = RemoveTrailingSpace(input);

                    input = RenderSubSection(input, "item", subType, tag, IncreasedIndent(indentSpaces), builder);

                    builder.AppendLine($"{indentSpaces}}}");
                }
                else if (subType.SpecialType == SpecialType.System_Boolean)
                {
                    builder.AppendLine($"{indentSpaces}if ({name}.{tag.ToString()})");
                    builder.AppendLine($"{indentSpaces}{{");

                    input = RemoveTrailingSpace(input);

                    input = RenderSubSection(input, name, type, tag, IncreasedIndent(indentSpaces), builder);

                    builder.AppendLine($"{indentSpaces}}}");
                }

                builder.AppendLine($"{indentSpaces}// End Section: {tag.ToString()}");
            }
            else if (tag[0] == '/')
            {
                tag = tag.Slice(1);
                // section end
                if (tag.SequenceEqual(sectionTag))
                {
                    input = RemoveTrailingSpace(input);
                    break;
                }
            }
            else
            {
                var tagName = tag.ToString();

                var symbol = type.GetMembers().OfType<IFieldSymbol>().Where(f => f.Name == tagName).FirstOrDefault()?.Type;
                if (symbol is null)
                {
                    symbol = type.GetMembers().OfType<IPropertySymbol>().Where(p => (p.GetMethod?.Name) == "get_" + tagName).FirstOrDefault()?.Type;
                }

                builder.AppendLine($"{indentSpaces}// Variable: {symbol} {name}.{tag.ToString()}");
                if (symbol is null)
                {
                    // Skip output
                }
                else if (symbol.SpecialType == SpecialType.System_Int32)
                {
                    builder.AppendLine($"{indentSpaces}output.WriteNumeric((uint){name}.{tag.ToString()});");
                }
                else if (symbol.SpecialType == SpecialType.System_String)
                {
                    builder.AppendLine($"{indentSpaces}output.WriteUtf8HtmlString({name}.{tag.ToString()});");
                }
                else
                {
                    builder.AppendLine($"{indentSpaces}output.WriteUtf8HtmlString({name}.{tag.ToString()}.ToString());");
                }
            }
        }

        return input;
    }

    private static ReadOnlySpan<char> RemoveTrailingSpace(ReadOnlySpan<char> input)
    {
        int offset;
        for (offset = 0; offset < input.Length; offset++)
        {
            var ch = input[offset];
            if (ch != '\n' && ch != '\r' && ch != ' ')
            {
                break;
            }
        }
        if (offset > 0)
        {
            input = input.Slice(offset);
        }

        return input;
    }

    private static string IncreasedIndent(string indentSpaces)
    {
        return indentSpaces + new string(' ', SpacesPerIndent);
    }

    private static void AddUtf8ByteArray(ReadOnlySpan<char> rawText, string indentSpaces, StringBuilder builder)
    {
        const int SpaceAt = 4;
        const int AdditionalSpaceAt = 8;
        const int WrapLineAt = 16;

        builder.Append(@"new byte[] {");

        if (rawText.Length > 0)
        {
            var output = 0;
            // Keep inline if only one line
            var isInline = rawText.Length <= WrapLineAt;
            foreach (var b in Encoding.UTF8.GetBytes(rawText.ToString()))
            {
                if (!isInline)
                {
                    if (output % WrapLineAt == 0)
                    {
                        builder.AppendLine();
                        builder.Append(indentSpaces);
                    }
                }

                if (output % WrapLineAt != 0)
                {
                    if (output % SpaceAt == 0)
                    {
                        builder.Append(' ');
                    }
                    if (output % AdditionalSpaceAt == 0)
                    {
                        builder.Append(' ');
                    }
                }

                builder.Append($"0x{b:x2},");
                output++;
            }
            // Replace the trailing comma; it is still valid C# without removing it
            // so its just about being tidy.
            if (isInline)
            {
                builder.Replace(',', '}', builder.Length - 1, 1);
            }
            else
            {
                builder.Replace(",", Environment.NewLine + indentSpaces.Substring(0, indentSpaces.Length - SpacesPerIndent) + "}", builder.Length - 1, 1);
            }
        }
        else
        {
            builder.Append("}");
        }
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
        => context.RegisterForSyntaxNotifications(() => new MustacheRenderReceiver());

    class MustacheRenderReceiver : ISyntaxReceiver
    {
        public Dictionary<string, List<InvocationExpressionSyntax>>? Invocations { get; private set; }

        public void OnVisitSyntaxNode(SyntaxNode node)
        {
            if (node.IsKind(SyntaxKind.InvocationExpression) &&
                node is InvocationExpressionSyntax invocation)
            {
                var expression = invocation.Expression;
                if (expression is MemberAccessExpressionSyntax member)
                {
                    var isMustache = false;
                    string? template = null;
                    if (member.IsKind(SyntaxKind.SimpleMemberAccessExpression))
                    {
                        foreach (SyntaxNode child in expression.ChildNodes())
                        {
                            if (!isMustache)
                            {
                                if (child is IdentifierNameSyntax classIdent)
                                {
                                    var valueText = classIdent.Identifier.ValueText;
                                    Console.Error.WriteLine(valueText);
                                    if (classIdent.Identifier.ValueText == "MustacheTemplates")
                                    {
                                        isMustache = true;
                                        continue;
                                    }
                                    else
                                    {
                                        break;
                                    }
                                }
                                else
                                {
                                    break;
                                }
                            }

                            if (child is IdentifierNameSyntax methodIdent)
                            {
                                var valueText = methodIdent.Identifier.ValueText;
                                if (valueText.IndexOf("Render", StringComparison.Ordinal) == 0)
                                {
                                    template = valueText.Substring("Render".Length);
                                }
                                break;
                            }
                        }

                        if (isMustache && template is not null)
                        {
                            if ((Invocations ??= new()).TryGetValue(template, out var list))
                            {
                                list.Add(invocation);
                            }
                            else
                            {
                                Invocations.Add(template, new() { invocation });
                            }
                        }
                    }
                }
            }
        }
    }
}

internal static class SourceGeneratorExtensions
{
    public static string? TryGetValue(this AnalyzerConfigOptions options, string key) =>
        options.TryGetValue(key, out var value) ? value : null;

    public static string? TryGetAdditionalFileMetadataValue(this AnalyzerConfigOptions options, string propertyName) =>
        options.TryGetValue($"build_metadata.AdditionalFiles.{propertyName}");
}