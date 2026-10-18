using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;

namespace BelemRestaurantes.Web.Models;

public sealed record RestaurantVideo(string Url, string Platform, string? EmbedUrl)
{
    public static RestaurantVideo? From(string? value)
    {
        if (value is null || value.Length > 500 || !Uri.TryCreate(value,UriKind.Absolute,out var uri) ||
            uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort) return null;
        var host=uri.IdnHost.ToLowerInvariant();
        var path=uri.AbsolutePath;
        string? id=null;
        if(host=="youtu.be")id=path.Trim('/');
        if(host is "youtube.com" or "www.youtube.com" or "m.youtube.com")
        {
            if(path=="/watch")id=QueryHelpers.ParseQuery(uri.Query).TryGetValue("v",out var video)?video.ToString():null;
            else if(path.StartsWith("/shorts/",StringComparison.Ordinal))id=path[8..].Trim('/');
        }
        if(id is not null) return Regex.IsMatch(id,"^[A-Za-z0-9_-]{11}$")
            ? new("https://www.youtube.com/watch?v="+id,"YouTube","https://www.youtube-nocookie.com/embed/"+id+"?rel=0") : null;
        if((host is "instagram.com" or "www.instagram.com") && Regex.IsMatch(path,"^/(reel|p|tv)/[A-Za-z0-9_-]+/?$")) return new(uri.GetLeftPart(UriPartial.Path),"Instagram",null);
        if((host is "tiktok.com" or "www.tiktok.com") && Regex.IsMatch(path,"^/@[A-Za-z0-9._-]+/video/[0-9]+/?$")) return new(uri.GetLeftPart(UriPartial.Path),"TikTok",null);
        if(host=="globoplay.globo.com" && Regex.IsMatch(path,"^/v/[0-9]+/?$"))return new(uri.GetLeftPart(UriPartial.Path),"Globoplay",null);
        return null;
    }
}
