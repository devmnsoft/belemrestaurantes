using System.Text.RegularExpressions;

namespace BelemRestaurantes.Infrastructure.Migrations;

/// <summary>
/// MySQL 8 does not accept MariaDB's ADD COLUMN IF NOT EXISTS. Keep the
/// historical scripts/checksums intact and execute their conditional additions
/// individually, under the migration runner's existing database lock.
/// This adapter is deliberately limited to the two immutable legacy scripts.
/// </summary>
public static class LegacyConditionalColumns
{
    public sealed record Addition(string Table,string Column,string Sql);

    public static IReadOnlyList<Addition>? Expand(string scriptId,string statement)
    {
        if(scriptId is not ("014" or "015"))return null;
        var alter=Regex.Match(statement,@"\AALTER\s+TABLE\s+restaurants\s+(?<clauses>ADD\s+COLUMN\s+IF\s+NOT\s+EXISTS[\s\S]+)\z",RegexOptions.IgnoreCase);
        if(!alter.Success)return null;
        var clauses=Regex.Split(alter.Groups["clauses"].Value,@",\s*(?=ADD\s+COLUMN\s+IF\s+NOT\s+EXISTS)",RegexOptions.IgnoreCase);
        var result=new List<Addition>();
        foreach(var clause in clauses)
        {
            var column=Regex.Match(clause.Trim(),@"\AADD\s+COLUMN\s+IF\s+NOT\s+EXISTS\s+(?<name>[a-z_][a-z0-9_]*)\s+(?<definition>[\s\S]+)\z",RegexOptions.IgnoreCase);
            if(!column.Success)throw new InvalidOperationException("DDL condicional legado não reconhecido. O script original foi preservado.");
            var name=column.Groups["name"].Value;
            result.Add(new("restaurants",name,$"ALTER TABLE `restaurants` ADD COLUMN `{name}` {column.Groups["definition"].Value}"));
        }
        return result;
    }
}
