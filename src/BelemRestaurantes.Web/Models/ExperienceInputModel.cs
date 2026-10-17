using System.ComponentModel.DataAnnotations;

namespace BelemRestaurantes.Web.Models;

public sealed class ExperienceInputModel
{
    public long Id { get; set; }

    [Required]
    public long RestaurantId { get; set; }

    [Range(1, 5)]
    public int Stars { get; set; } = 5;

    [Range(1, 5)]
    public int ServiceStars { get; set; } = 5;

    [Range(1, 5)]
    public int FlavorStars { get; set; } = 5;

    [Range(1, 5)]
    public int AmbianceStars { get; set; } = 5;

    [Range(1, 5)]
    public int CostBenefitStars { get; set; } = 5;

    [MaxLength(3000)]
    public string? ReviewText { get; set; }

    [MaxLength(500)]
    public string? DishesConsumed { get; set; }

    [Range(0, 99999)]
    public decimal? PricePaid { get; set; }

    [Required]
    public string Visibility { get; set; } = "public"; // public, private, unlisted
}
