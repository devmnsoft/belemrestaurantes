using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Domain.Entities;
using BelemRestaurantes.Domain.Rules;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Infrastructure.Partner;

public sealed class MySqlMenuService(
    MySqlConnectionFactory connectionFactory,
    IAiRouter aiRouter,
    ILogger<MySqlMenuService> logger) : IMenuService
{
    public async Task<IReadOnlyList<MenuCategory>> GetRestaurantMenuAsync(long restaurantId, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        const string catSql = "SELECT * FROM menu_categories WHERE restaurant_id = @restaurantId AND is_active = TRUE ORDER BY display_order ASC, name ASC;";
        const string itemSql = "SELECT * FROM menu_items WHERE restaurant_id = @restaurantId ORDER BY display_order ASC, name ASC;";

        var categories = (await connection.QueryAsync<MenuCategory>(new CommandDefinition(catSql, new { restaurantId }, cancellationToken: ct))).ToList();
        var items = (await connection.QueryAsync<MenuItem>(new CommandDefinition(itemSql, new { restaurantId }, cancellationToken: ct))).ToList();

        var itemLookup = items.ToLookup(i => i.CategoryId);
        foreach (var cat in categories)
        {
            cat.Items = itemLookup[cat.Id].ToList();
        }

        return categories;
    }

    public async Task<long> AddCategoryAsync(long restaurantId, string name, int displayOrder, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        const string sql = @"
            INSERT INTO menu_categories (restaurant_id, name, display_order, is_active)
            VALUES (@restaurantId, @name, @displayOrder, TRUE);
            SELECT LAST_INSERT_ID();";
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(sql, new { restaurantId, name, displayOrder }, cancellationToken: ct));
    }

    public async Task<bool> UpdateCategoryAsync(long restaurantId, long categoryId, string name, int displayOrder, bool isActive, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        const string sql = @"
            UPDATE menu_categories
            SET name = @name, display_order = @displayOrder, is_active = @isActive
            WHERE id = @categoryId AND restaurant_id = @restaurantId;";
        return await connection.ExecuteAsync(new CommandDefinition(sql, new { restaurantId, categoryId, name, displayOrder, isActive }, cancellationToken: ct)) > 0;
    }

    public async Task<bool> DeleteCategoryAsync(long restaurantId, long categoryId, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        const string sql = "DELETE FROM menu_categories WHERE id = @categoryId AND restaurant_id = @restaurantId;";
        return await connection.ExecuteAsync(new CommandDefinition(sql, new { restaurantId, categoryId }, cancellationToken: ct)) > 0;
    }

    public async Task<long> AddMenuItemAsync(MenuItem item, CancellationToken ct)
    {
        item.IsRegionalDish = PartnerRules.IsRecognizedRegionalDish(item.Name);
        item.RegionalDishType = PartnerRules.ClassifyRegionalType(item.Name);

        await using var connection = await connectionFactory.OpenAsync(ct);
        const string sql = @"
            INSERT INTO menu_items
            (category_id, restaurant_id, name, description, price, image_url, ingredients, allergens, is_available, is_regional_dish, regional_dish_type, display_order, created_at, updated_at)
            VALUES
            (@CategoryId, @RestaurantId, @Name, @Description, @Price, @ImageUrl, @Ingredients, @Allergens, @IsAvailable, @IsRegionalDish, @RegionalDishType, @DisplayOrder, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));
            SELECT LAST_INSERT_ID();";

        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(sql, item, cancellationToken: ct));
    }

    public async Task<bool> UpdateMenuItemAsync(MenuItem item, CancellationToken ct)
    {
        item.IsRegionalDish = PartnerRules.IsRecognizedRegionalDish(item.Name);
        item.RegionalDishType = PartnerRules.ClassifyRegionalType(item.Name);

        await using var connection = await connectionFactory.OpenAsync(ct);
        const string sql = @"
            UPDATE menu_items
            SET category_id = @CategoryId, name = @Name, description = @Description, price = @Price, image_url = @ImageUrl,
                ingredients = @Ingredients, allergens = @Allergens, is_available = @IsAvailable, is_regional_dish = @IsRegionalDish,
                regional_dish_type = @RegionalDishType, display_order = @DisplayOrder, updated_at = UTC_TIMESTAMP(6)
            WHERE id = @Id AND restaurant_id = @RestaurantId;";

        return await connection.ExecuteAsync(new CommandDefinition(sql, item, cancellationToken: ct)) > 0;
    }

    public async Task<bool> DeleteMenuItemAsync(long restaurantId, long itemId, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        const string sql = "DELETE FROM menu_items WHERE id = @itemId AND restaurant_id = @restaurantId;";
        return await connection.ExecuteAsync(new CommandDefinition(sql, new { restaurantId, itemId }, cancellationToken: ct)) > 0;
    }

    public async Task<string> SuggestDishDescriptionWithAiAsync(string dishName, string? ingredients, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dishName)) return string.Empty;

        var systemPrompt = @"Você é a Cuia IA, assistente gastronômica paraense do Belém Restaurantes.
Sua missão é gerar uma descrição apetitosa, culturalmente rica e concisa (máximo 2 frases) para o prato informado.
REGRAS OBRIGATÓRIAS:
1. Respeite as tradições gastronômicas do Pará e da Amazônia.
2. NUNCA invente ingredientes não mencionados nem invente preços ou disponibilidade.
3. Se for prato típico como tacacá, maniçoba, filhote, açaí ou pato no tucupi, valorize as raízes paraenses.
4. Responda em texto puro e direto.";

        var userPayload = $"Prato: {dishName}. Ingredientes informados: {ingredients ?? "não informados"}.";

        try
        {
            var classification = await aiRouter.ClassifyAsync("dish_description", $"{systemPrompt}\n{userPayload}", ct);
            if (!string.IsNullOrWhiteSpace(classification?.Category))
            {
                return classification.Category;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cuia IA não disponível para descrição; aplicando template regional.");
        }

        // Fallback baseado em tradição culinária paraense caso IA esteja sem chave configurada
        var type = PartnerRules.ClassifyRegionalType(dishName);
        return type switch
        {
            "tacaca" => "Caldo de tucupi fervente temperado com jambu que amortece a boca, camarões secos selecionados e goma de mandioca tradicional.",
            "manicoba" => "Prato sagrado da culinária paraense preparado com folhas de maniva moídas e cozidas lentamente por 7 dias, servido com carnes nobres e arroz branco.",
            "pato_tucupi" => "Pato assado e desfiado mergulhado em tucupi perfumado com folhas de chicória do Pará, alfavaca e jambu fresco da horta.",
            "acai" => "Açaí puro e espesso batido na hora, servido na tradicional cuia paraense acompanhado de farinha d'água de Bragança ou tapioca.",
            "peixe_regional" => "Peixe fresco amazônico de carne nobre e macia, grelhado com temperos regionais e servido com molho especial de tucupi e castanha-do-pará.",
            "sobremesa_paraense" => "Sobremesa artesanal com a acidez marcante e aroma inconfundível dos frutos nativos da floresta amazônica.",
            _ => $"Especialidade preparada com ingredientes selecionados, tempero equilibrado e o autêntico sabor da culinária paraense."
        };
    }
}

public sealed class MySqlMonetizationService(
    MySqlConnectionFactory connectionFactory,
    ILogger<MySqlMonetizationService> logger) : IMonetizationService
{
    private const string MnsoftPixCnpj = "18.160.057/0001-13";

    public async Task<IReadOnlyList<CommercialPlan>> ListPlansAsync(CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        const string sql = "SELECT * FROM commercial_plans WHERE is_active = TRUE ORDER BY monthly_price ASC;";
        var list = await connection.QueryAsync<CommercialPlan>(new CommandDefinition(sql, cancellationToken: ct));
        return list.ToList();
    }

    public async Task<RestaurantSubscription?> GetActiveSubscriptionAsync(long restaurantId, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        const string sql = @"
            SELECT * FROM restaurant_subscriptions
            WHERE restaurant_id = @restaurantId AND status = 'active'
            ORDER BY id DESC LIMIT 1;";
        return await connection.QueryFirstOrDefaultAsync<RestaurantSubscription>(new CommandDefinition(sql, new { restaurantId }, cancellationToken: ct));
    }

    public async Task<MerchantOrder> CreateOrderAsync(long restaurantId, string userId, string planCode, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        const string planSql = "SELECT * FROM commercial_plans WHERE code = @planCode AND is_active = TRUE;";
        var plan = await connection.QueryFirstOrDefaultAsync<CommercialPlan>(new CommandDefinition(planSql, new { planCode }, cancellationToken: ct))
                   ?? throw new InvalidOperationException($"Plano '{planCode}' não encontrado.");

        var orderNumber = $"BLM-{DateTimeOffset.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..24].ToUpperInvariant();
        var pixPayload = $"00020126580014BR.GOV.BCB.PIX0114{Regex.Replace(MnsoftPixCnpj, @"[^\d]", "")}52040000530398654{plan.MonthlyPrice:F2}5802BR5915MNSOFT BELEM6005BELEM62070503***6304";

        var order = new MerchantOrder
        {
            OrderNumber = orderNumber,
            RestaurantId = restaurantId,
            UserId = userId,
            PlanCode = planCode,
            Amount = plan.MonthlyPrice,
            PaymentStatus = "pending",
            PaymentMethod = "pix",
            PixCnpj = MnsoftPixCnpj,
            PixCopyPaste = pixPayload,
            CreatedAt = DateTimeOffset.UtcNow
        };

        const string insertSql = @"
            INSERT INTO merchant_orders
            (order_number, restaurant_id, user_id, plan_code, amount, payment_status, payment_method, pix_cnpj, pix_copy_paste, created_at)
            VALUES
            (@OrderNumber, @RestaurantId, @UserId, @PlanCode, @Amount, 'pending', 'pix', @PixCnpj, @PixCopyPaste, UTC_TIMESTAMP(6));
            SELECT LAST_INSERT_ID();";

        order.Id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(insertSql, order, cancellationToken: ct));
        return order;
    }

    public async Task<bool> ConfirmPaymentAsync(string orderNumber, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        try
        {
            const string getOrderSql = "SELECT * FROM merchant_orders WHERE order_number = @orderNumber FOR UPDATE;";
            var order = await connection.QueryFirstOrDefaultAsync<MerchantOrder>(new CommandDefinition(getOrderSql, new { orderNumber }, transaction, cancellationToken: ct));
            if (order == null || order.PaymentStatus == "paid") return false;

            const string updateOrderSql = @"
                UPDATE merchant_orders
                SET payment_status = 'paid', paid_at = UTC_TIMESTAMP(6)
                WHERE id = @Id;";
            await connection.ExecuteAsync(new CommandDefinition(updateOrderSql, new { order.Id }, transaction, cancellationToken: ct));

            const string updateSubSql = @"
                INSERT INTO restaurant_subscriptions
                (restaurant_id, user_id, plan_code, status, started_at, expires_at, payment_method)
                VALUES
                (@RestaurantId, @UserId, @PlanCode, 'active', UTC_TIMESTAMP(6), DATE_ADD(UTC_TIMESTAMP(6), INTERVAL 30 DAY), 'pix')
                ON DUPLICATE KEY UPDATE plan_code = VALUES(plan_code), status = 'active', expires_at = DATE_ADD(UTC_TIMESTAMP(6), INTERVAL 30 DAY);";

            await connection.ExecuteAsync(new CommandDefinition(updateSubSql, new { order.RestaurantId, order.UserId, order.PlanCode }, transaction, cancellationToken: ct));

            await transaction.CommitAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            logger.LogError(ex, "Erro ao confirmar pagamento do pedido {OrderNumber}", orderNumber);
            throw;
        }
    }

    public async Task<IReadOnlyList<RestaurantCoupon>> ListCouponsAsync(long restaurantId, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        const string sql = "SELECT * FROM restaurant_coupons WHERE restaurant_id = @restaurantId ORDER BY created_at DESC;";
        var list = await connection.QueryAsync<RestaurantCoupon>(new CommandDefinition(sql, new { restaurantId }, cancellationToken: ct));
        return list.ToList();
    }

    public async Task<long> CreateCouponAsync(RestaurantCoupon coupon, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        const string sql = @"
            INSERT INTO restaurant_coupons
            (restaurant_id, code, title, description, discount_percentage, discount_amount, valid_until, is_active, created_at)
            VALUES
            (@RestaurantId, @Code, @Title, @Description, @DiscountPercentage, @DiscountAmount, @ValidUntil, TRUE, UTC_TIMESTAMP(6));
            SELECT LAST_INSERT_ID();";
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(sql, coupon, cancellationToken: ct));
    }

    public async Task<bool> ToggleCouponAsync(long restaurantId, long couponId, bool isActive, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        const string sql = "UPDATE restaurant_coupons SET is_active = @isActive WHERE id = @couponId AND restaurant_id = @restaurantId;";
        return await connection.ExecuteAsync(new CommandDefinition(sql, new { restaurantId, couponId, isActive }, cancellationToken: ct)) > 0;
    }
}

public sealed partial class MySqlMobilityTrailService(MySqlConnectionFactory connectionFactory) : IMobilityTrailService, IEditorialTrails
{
    public async Task<IReadOnlyList<GastronomicTrail>> ListTrailsAsync(CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        const string sql = "SELECT * FROM gastronomic_trails WHERE (editorial_json IS NOT NULL OR EXISTS(SELECT 1 FROM trail_stops ts WHERE ts.trail_id=gastronomic_trails.id)) AND is_active = TRUE AND (editorial_json IS NULL OR editorial_state='published') ORDER BY editorial_json IS NULL, region ASC, title ASC;";
        var list = (await connection.QueryAsync<GastronomicTrail>(new CommandDefinition(sql, cancellationToken: ct))).ToList();

        return list;
    }

    public async Task<GastronomicTrail?> GetTrailBySlugAsync(string slug, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        const string trailSql = "SELECT * FROM gastronomic_trails WHERE slug = @slug AND is_active = TRUE AND (editorial_json IS NULL OR editorial_state='published');";
        var trail = await connection.QueryFirstOrDefaultAsync<GastronomicTrail>(new CommandDefinition(trailSql, new { slug }, cancellationToken: ct));
        if (trail == null) return null;

        const string stopsSql = @"
            SELECT s.*, r.name as RestaurantName, r.address as RestaurantAddress, r.category as RestaurantCategory, r.photo_url as RestaurantPhotoUrl
            FROM trail_stops s
            JOIN restaurants r ON r.id = s.restaurant_id
            WHERE s.trail_id = @Id AND r.status='published'
            ORDER BY s.stop_order ASC;";

        trail.Stops = (await connection.QueryAsync<TrailStop>(new CommandDefinition(stopsSql, new { trail.Id }, cancellationToken: ct))).ToList();
        return trail;
    }

    public async Task<string?> GetTransitDirectionsAsync(long restaurantId, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        const string sql = "SELECT address, neighborhood, city FROM restaurants WHERE id = @restaurantId;";
        var r = await connection.QueryFirstOrDefaultAsync<(string? address, string? neighborhood, string city)>(new CommandDefinition(sql, new { restaurantId }, cancellationToken: ct));
        if (r.address == null) return null;

        return $"Confirme o deslocamento até {r.city} no Cuia Rotas. Não há linha, tarifa ou horário de transporte confirmado por esta ficha.";
    }

}
