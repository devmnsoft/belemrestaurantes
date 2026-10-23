using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BelemRestaurantes.Web.Controllers;

[Authorize(Policy="RestaurantModerator"),Route("admin/roteiros")]
[ResponseCache(Location=ResponseCacheLocation.None,NoStore=true)]
public sealed class AdminTrailsController(IEditorialTrails repository):Controller
{
    [HttpGet] public async Task<IActionResult> Index(CancellationToken ct)=>View(await repository.ListForEditingAsync(ct));
    [HttpGet("{id:int}")] public async Task<IActionResult> Edit(int id,CancellationToken ct)
    {
        var row=await repository.GetForEditingAsync(id,ct);if(row is null)return NotFound();var e=row.Editorial;
        var model=new TrailEditInput{Id=id,Version=row.EditorialVersion,Title=row.Title,Description=row.Description,DurationHours=row.EstimatedDurationHours,Audience=e?.Audience??"",Origin=e?.Origin??"",Mode=e?.Mode??"urban",BeforeYouGo=e?.BeforeYouGo??"",Accessibility=e?.Accessibility??"",Stops=e?.Stops.Select(s=>new TrailStopInput{Name=s.Name,City=s.City,Summary=s.Summary,SourceUrl=s.SourceUrl,SourceName=s.SourceName}).ToList()??[]};
        while(model.Stops.Count<5)model.Stops.Add(new());return View(model);
    }
    [HttpPost("{id:int}"),ValidateAntiForgeryToken,RequestSizeLimit(32768)]
    public async Task<IActionResult> Save(int id,TrailEditInput input,CancellationToken ct)
    {
        input.Id=id;if(!ModelState.IsValid)return View("Edit",input);
        var steps=input.Stops.Where(s=>!string.IsNullOrWhiteSpace(s.Name)||!string.IsNullOrWhiteSpace(s.SourceUrl)||!string.IsNullOrWhiteSpace(s.Summary)||!string.IsNullOrWhiteSpace(s.SourceName)).Select(s=>new EditorialTrailStop(s.Name??"",s.City??"",s.Summary??"",s.SourceUrl??"",s.SourceName??"")).ToArray();
        try
        {
            if(!await repository.SaveDraftAsync(id,input.Title,input.Description,input.DurationHours,new(input.Audience,input.Origin,input.Mode,input.BeforeYouGo,input.Accessibility,steps),input.Version,User.FindFirstValue(ClaimTypes.NameIdentifier)!,ct))return Conflict("Este roteiro mudou em outra sessão. Reabra antes de salvar.");
        }
        catch(ArgumentException ex){ModelState.AddModelError("",ex.Message);return View("Edit",input);}
        TempData["Flash"]="Rascunho salvo. Confira as fontes e publique após revisar.";return RedirectToAction(nameof(Index));
    }
    [HttpPost("{id:int}/publicacao"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(int id,bool published,int version,CancellationToken ct)
    {
        try{if(!await repository.SetPublishedAsync(id,published,version,User.FindFirstValue(ClaimTypes.NameIdentifier)!,ct))return Conflict("O roteiro mudou. Atualize a página antes de decidir.");}
        catch(ArgumentException ex){TempData["Flash"]=ex.Message;}
        return RedirectToAction(nameof(Index));
    }
}

public sealed class TrailEditInput
{
    public int Id{get;set;} public int Version{get;set;}
    [Required,StringLength(150,MinimumLength=3)] public string Title{get;set;}="";
    [Required,StringLength(1500,MinimumLength=10)] public string Description{get;set;}="";
    [Range(1,24)]public int DurationHours{get;set;}=3;
    [Required,StringLength(400)]public string Audience{get;set;}="";
    [Required,StringLength(300)]public string Origin{get;set;}="";
    public string Mode{get;set;}="urban";
    [Required,StringLength(1600)]public string BeforeYouGo{get;set;}="";
    [Required,StringLength(800)]public string Accessibility{get;set;}="";
    [Required,MinLength(3),MaxLength(5)]public List<TrailStopInput> Stops{get;set;}=[];
}
public sealed class TrailStopInput
{
    [StringLength(160)]public string? Name{get;set;}="";
    [StringLength(100)]public string? City{get;set;}="Belém";
    [StringLength(1200)]public string? Summary{get;set;}="";
    [StringLength(1800)]public string? SourceUrl{get;set;}="";
    [StringLength(160)]public string? SourceName{get;set;}="";
}
