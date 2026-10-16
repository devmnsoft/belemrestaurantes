using System.Text;

namespace BelemRestaurantes.Infrastructure.Migrations;

/// <summary>
/// Divide um script SQL em comandos individuais respeitando literais,
/// identificadores e comentários. Sem isso, um `;` dentro de um texto
/// quebraria a migração silenciosamente.
/// </summary>
public static class SqlScriptSplitter
{
    public static IReadOnlyList<string> Split(string script)
    {
        ArgumentNullException.ThrowIfNull(script);

        var statements = new List<string>();
        var current = new StringBuilder();
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var inBacktick = false;
        var inLineComment = false;
        var inBlockComment = false;

        for (var i = 0; i < script.Length; i++)
        {
            var ch = script[i];
            var next = i + 1 < script.Length ? script[i + 1] : '\0';

            if (inLineComment)
            {
                if (ch is '\n')
                {
                    inLineComment = false;
                    current.Append(ch);
                }

                continue;
            }

            if (inBlockComment)
            {
                if (ch == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }

                continue;
            }

            if (!inSingleQuote && !inDoubleQuote && !inBacktick)
            {
                if (ch == '-' && next == '-')
                {
                    inLineComment = true;
                    i++;
                    continue;
                }

                if (ch == '#')
                {
                    inLineComment = true;
                    continue;
                }

                if (ch == '/' && next == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }

                if (ch == ';')
                {
                    AddIfNotEmpty(statements, current);
                    continue;
                }
            }

            // Escape de literal: em MySQL tanto '' quanto \' escapam o literal.
            if ((inSingleQuote || inDoubleQuote) && ch == '\\' && next != '\0')
            {
                current.Append(ch);
                current.Append(next);
                i++;
                continue;
            }

            if (ch == '\'' && !inDoubleQuote && !inBacktick)
            {
                if (inSingleQuote && next == '\'')
                {
                    // '' dentro do literal representa uma aspa.
                    current.Append(ch).Append(next);
                    i++;
                    continue;
                }

                inSingleQuote = !inSingleQuote;
            }
            else if (ch == '"' && !inSingleQuote && !inBacktick)
            {
                inDoubleQuote = !inDoubleQuote;
            }
            else if (ch == '`' && !inSingleQuote && !inDoubleQuote)
            {
                inBacktick = !inBacktick;
            }

            current.Append(ch);
        }

        AddIfNotEmpty(statements, current);
        return statements;
    }

    private static void AddIfNotEmpty(List<string> statements, StringBuilder buffer)
    {
        var text = buffer.ToString().Trim();
        if (text.Length > 0)
        {
            statements.Add(text);
        }

        buffer.Clear();
    }
}