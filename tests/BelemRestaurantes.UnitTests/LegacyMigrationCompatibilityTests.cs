using BelemRestaurantes.Infrastructure.Migrations;

namespace BelemRestaurantes.UnitTests;
public sealed class LegacyMigrationCompatibilityTests
{
    [Theory][InlineData("014",3)][InlineData("015",4)]
    public void HistoricalConditionalColumnsExpandWithoutChangingChecksums(string id,int count)
    {
        var script=MigrationRunner.LoadScripts().Single(s=>s.Id==id);var before=script.ComputeChecksum();
        var additions=SqlScriptSplitter.Split(script.Sql).SelectMany(sql=>LegacyConditionalColumns.Expand(id,sql)??[]).ToArray();
        Assert.Equal(count,additions.Length);Assert.Equal(count,additions.Select(a=>a.Column).Distinct().Count());
        Assert.All(additions,a=>{Assert.Equal("restaurants",a.Table);Assert.DoesNotContain("IF NOT EXISTS",a.Sql);});
        Assert.Equal(before,script.ComputeChecksum());
        if(id=="014")Assert.Contains("ENUM('operational', 'temporarily_closed', 'permanently_closed')",additions[1].Sql);
    }
    [Fact] public void AdapterDoesNotRewriteOtherScriptsOrOrdinaryDdl()
    {
        Assert.Null(LegacyConditionalColumns.Expand("023","ALTER TABLE restaurants ADD COLUMN IF NOT EXISTS other INT"));
        Assert.Null(LegacyConditionalColumns.Expand("014","CREATE TABLE IF NOT EXISTS example(id INT)"));
    }
}
