using Microsoft.AspNetCore.Mvc;
namespace BelemRestaurantes.Web.Controllers;
public sealed class HomeController : Controller {public IActionResult Index()=>View();public IActionResult Error()=>Problem(statusCode:500,title:"Não foi possível concluir esta solicitação");}