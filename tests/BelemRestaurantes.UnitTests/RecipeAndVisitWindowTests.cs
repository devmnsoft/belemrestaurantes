using System.Text.Json;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Application.Services;
using BelemRestaurantes.Infrastructure.MySql;
namespace BelemRestaurantes.UnitTests;
public sealed class RecipeAndVisitWindowTests
{
    [Fact]
    public void EditorialCollectionHasRealInstructionsAndExplicitInspiredIdentity()
    {
        var assembly=typeof(MySqlParaRecipes).Assembly;
        using var stream=assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n=>n.EndsWith("Editorial.para-recipes.json",StringComparison.Ordinal)))!;
        var recipes=JsonSerializer.Deserialize<ParaRecipe[]>(stream)!;
        Assert.True(recipes.Length>=12);
        Assert.Equal(recipes.Length,recipes.Select(r=>r.Slug).Distinct().Count());
        foreach(var recipe in recipes){RecipeRules.Validate(recipe);Assert.Equal("inspired",recipe.Kind);Assert.True(recipe.Steps.Length>=3);Assert.DoesNotContain("Roxy",recipe.Story??"");}
        var cream=Assert.Single(recipes,r=>r.Slug=="creme-de-cupuacu");Assert.Equal(120,cream.WaitMinutes);Assert.Equal(0,cream.CookMinutes);
    }
    private static ParaRecipe Recipe()=>new(){Slug="receita-teste",Title="Teste",Subtitle="Preparo editorial",Ingredients=["Água"],Steps=["Aquecer"]};
    [Fact]public void OfficialRecipeNeedsBothSourceAndAuthorization()
    {var r=Recipe();r.Kind="official";Assert.Throws<ArgumentException>(()=>RecipeRules.Validate(r));r.SourceUrl="https://example.org/receita";Assert.Throws<ArgumentException>(()=>RecipeRules.Validate(r));r.PermissionNote="Publicação oficial conferida pela curadoria.";RecipeRules.Validate(r);}
    [Theory][InlineData("javascript:alert(1)")][InlineData("https://youtube.com.attacker.test/video")][InlineData("https://user:password@youtube.com/watch?v=1")][InlineData("http://youtu.be/test")]
    public void ExternalVideoRequiresAllowedPublicHttpsPlatform(string url)
    {var r=Recipe();r.VideoUrl=url;r.VideoTitle="Vídeo";Assert.Throws<ArgumentException>(()=>RecipeRules.Validate(r));}
    [Fact]public void VideoNeedsTitleAndRecipeSlugCannotInjectMarkup()
    {var r=Recipe();r.VideoUrl="https://youtu.be/test";Assert.Throws<ArgumentException>(()=>RecipeRules.Validate(r));r.VideoUrl=null;r.Slug="<script>alert(1)</script>";Assert.Throws<ArgumentException>(()=>RecipeRules.Validate(r));}
    private static readonly DateTimeOffset Now=new(2026,10,19,12,0,0,TimeSpan.FromHours(-3));
    private static OperatingSchedule Schedule(params OpeningWindow[] windows)=>new(new(){ConfirmedAt=Now.UtcDateTime.AddDays(-1),ExpiresAt=Now.UtcDateTime.AddDays(8),SourceUrl="https://example.org/hours",Authority="owner"},Enumerable.Range(0,7).Select(day=>new OpeningDay(day,false,false,windows)).ToArray());
    [Fact]public void UnknownOrExpiredHoursDoNotBecomeSuggestedVisitTimes()
    {Assert.Empty(VisitWindows.Suggest(new(null,[]),Now,Now));var schedule=Schedule(new OpeningWindow(660,900));schedule.Info!.ExpiresAt=Now.UtcDateTime.AddMinutes(-1);Assert.Empty(VisitWindows.Suggest(schedule,Now,Now));}
    [Fact]public void VisitCannotBridgeLunchBreakAndRespectsRequestedDuration()
    {var schedule=Schedule(new OpeningWindow(660,750),new(810,900));Assert.Empty(VisitWindows.Suggest(schedule,Now,Now,"lunch",120));var results=VisitWindows.Suggest(schedule,Now,Now,"lunch",30);Assert.NotEmpty(results);Assert.All(results,w=>{Assert.Equal(30,(w.End-w.Start).TotalMinutes);Assert.Equal("open",OpeningHours.Evaluate(schedule,w.Start,Now).State);Assert.True(OpeningHours.Evaluate(schedule,w.Start,Now).NextChange>=w.End);});}
    [Fact]public void FutureQueryBeyondSourceValidityIsUnknown()
    {var schedule=Schedule(new OpeningWindow(0,1439));schedule.Info!.ExpiresAt=Now.UtcDateTime.AddHours(1);Assert.Equal("unknown",OpeningHours.Evaluate(schedule,Now.AddDays(1),Now).State);Assert.Empty(VisitWindows.Suggest(schedule,Now.AddDays(1),Now));}
    [Fact]public void ExceptionAndReservationArePreservedWithoutCrowdClaims()
    {var schedule=Schedule(new OpeningWindow(660,900));schedule.Info!.ReservationOnly=true;var days=schedule.Days.Append(new OpeningDay(1,true,false,[],new DateOnly(2026,10,19))).ToArray();var results=VisitWindows.Suggest(schedule with{Days=days},Now,Now,"lunch",60);Assert.NotEmpty(results);Assert.All(results,w=>{Assert.NotEqual(Now.Date,TimeZoneInfo.ConvertTime(w.Start,OpeningHours.ParaZone).Date);Assert.Contains("reserva",w.Note);});}
}
