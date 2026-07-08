using DeltaZulu.Agent.SchemaMetadata;

namespace DeltaZulu.Agent.ProfileWorkbench;

public sealed record WorkbenchValidationResult(bool IsValid, string? Error = null);

public static class WorkbenchQueryValidator
{
    public static WorkbenchValidationResult Validate(string query, SchemaDescriptor schema)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new WorkbenchValidationResult(false, "query is empty.");
        }

        var normalized = query.Replace("\\n", Environment.NewLine, StringComparison.Ordinal).Trim();
        if (ContainsStatementSeparatorOutsideStrings(normalized))
        {
            return new WorkbenchValidationResult(false, "multiple KQL statements are not supported in the profile workbench.");
        }

        if (ContainsRenderOperatorOutsideStrings(normalized))
        {
            return new WorkbenchValidationResult(false, "render is not supported in dzagentctl. Visualizations belong to DeltaZulu.Platform.");
        }

        var firstToken = normalized.Split([' ', '\t', '\r', '\n', '|'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (firstToken is null)
        {
            return new WorkbenchValidationResult(false, "query must start with the profile table.");
        }

        if (!firstToken.Equals(schema.Table, StringComparison.OrdinalIgnoreCase))
        {
            return new WorkbenchValidationResult(false, $"query must start with table '{schema.Table}'.");
        }

        return new WorkbenchValidationResult(true);
    }

    private static bool ContainsStatementSeparatorOutsideStrings(string query)
    {
        var inSingle = false;
        var inDouble = false;
        foreach (var c in query)
        {
            if (c == '\'' && !inDouble)
            {
                inSingle = !inSingle;
                continue;
            }

            if (c == '"' && !inSingle)
            {
                inDouble = !inDouble;
                continue;
            }

            if (c == ';' && !inSingle && !inDouble)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsRenderOperatorOutsideStrings(string query)
    {
        var tokens = TokensOutsideStrings(query);
        for (var i = 0; i < tokens.Count - 1; i++)
        {
            if (tokens[i] == "|" && tokens[i + 1].Equals("render", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> TokensOutsideStrings(string query)
    {
        var tokens = new List<string>();
        var current = new List<char>();
        var inSingle = false;
        var inDouble = false;

        void Flush()
        {
            if (current.Count == 0)
            {
                return;
            }

            tokens.Add(new string([.. current]));
            current.Clear();
        }

        foreach (var c in query)
        {
            if (c == '\'' && !inDouble)
            {
                inSingle = !inSingle;
                continue;
            }

            if (c == '"' && !inSingle)
            {
                inDouble = !inDouble;
                continue;
            }

            if (inSingle || inDouble)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                Flush();
                continue;
            }

            if (c == '|')
            {
                Flush();
                tokens.Add("|");
                continue;
            }

            current.Add(c);
        }

        Flush();
        return tokens;
    }
}
