using BelemRestaurantes.Application.Models;
using Microsoft.AspNetCore.Http;

namespace BelemRestaurantes.Web.Models;

public static class ModerationNavigation
{
    public static Dictionary<string,string?> Fields(ModerationFilter f) => new()
    {
        ["Term"]=f.Term,["City"]=f.City,["Category"]=f.Category,["Source"]=f.Source,
        ["Completeness"]=f.Completeness,["Duplicate"]=f.Duplicate,
        ["From"]=f.From?.ToString("yyyy-MM-dd"),["Until"]=f.Until?.ToString("yyyy-MM-dd"),
        ["Stage"]=f.Stage is "approved" or "published" or "rejected" or "archived" or "blocked" ? f.Stage : "pending",
        ["Sort"]=f.Sort is "name" or "recent" or "city" ? f.Sort : "oldest",["PageSize"]=f.SafePageSize.ToString()
    };
    public static string PageUrl(ModerationFilter filter,int page)
    {
        var fields=Fields(filter);fields["page"]=Math.Max(1,page).ToString();
        return "/admin/moderacao"+QueryString.Create(fields);
    }
    public static string SelectionKey(ModerationFilter filter)
    {
        var fields=Fields(filter);fields.Remove("PageSize");fields.Remove("Sort");
        return QueryString.Create(fields).ToString();
    }
    public static string SafeReturn(string? path) => path is not null && path.Length<=4096 && !path.Any(char.IsControl) &&
        (path=="/admin/moderacao" || path.StartsWith("/admin/moderacao?",StringComparison.Ordinal)) ? path : "/admin/moderacao";
    public static string StageLabel(string stage) => stage switch
    {
        "approved"=>"Aprovado, aguardando publicação", "published"=>"Publicado", "rejected"=>"Rejeitado",
        "archived"=>"Arquivado", "blocked"=>"Bloqueado", _=>"Pendente de revisão"
    };
    public static string OperationLabel(string operation) => operation switch
    {
        "approve"=>"Aprovar e publicar", "approve_only"=>"Aprovar sem publicar", "publish"=>"Publicar aprovados",
        "archive"=>"Arquivar", "block"=>"Bloquear", "reject"=>"Rejeitar", "review"=>"Solicitar revisão",
        "duplicate"=>"Marcar possível duplicidade", "reprocess"=>"Reprocessar dados", _=>operation
    };
}
