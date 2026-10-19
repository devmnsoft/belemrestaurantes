using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BelemRestaurantes.Application.Abstractions;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace BelemRestaurantes.Application.Services;

public sealed class CuiaAiService(
    IRestaurantReadRepository restaurantRepo,
    IParaRecipes recipesRepo,
    IEnumerable<IAiProvider> aiProviders,
    ILogger<CuiaAiService> logger) : ICuiaAiService
{
    private readonly IAiProvider? _geminiProvider =
        aiProviders.FirstOrDefault(p => p.Provider == AiProvider.Gemini && p.IsConfigured)
        ?? aiProviders.FirstOrDefault(p => p.IsConfigured);

    public async Task<CuiaAiResponse> AskAsync(CuiaAiQuery query, CancellationToken ct)
    {
        var rawPrompt = (query.Prompt ?? "").Trim();
        var prompt = rawPrompt.Length > 500 ? rawPrompt[..500] : rawPrompt;
        var pLower = prompt.ToLowerInvariant();

        // 1. Identificar intenções culinárias e geográficas
        var detectedCategory = DetectCategory(pLower);
        var detectedCity = !string.IsNullOrWhiteSpace(query.City) ? query.City : DetectCity(pLower);
        var isRiver = pLower.Contains("combu") || pLower.Contains("barco") || pLower.Contains("ilha") || pLower.Contains("rio");
        var isTacaca = pLower.Contains("tacacá") || pLower.Contains("tacaca") || pLower.Contains("jambu") || pLower.Contains("tucupi");
        var isAcai = pLower.Contains("açaí") || pLower.Contains("acai") || pLower.Contains("peixe frito");
        var isManicoba = pLower.Contains("maniçoba") || pLower.Contains("manicoba");
        var isFish = pLower.Contains("peixe") || pLower.Contains("filhote") || pLower.Contains("pirarucu") || pLower.Contains("tambaqui");
        var isDocas = pLower.Contains("docas") || pLower.Contains("estação das docas");
        var isVeroPeso = pLower.Contains("ver-o-peso") || pLower.Contains("mercado") || pLower.Contains("feira");
        var isVegetarian = pLower.Contains("vegetariano") || pLower.Contains("vegano") || (query.Dietary?.Contains("veg", StringComparison.OrdinalIgnoreCase) == true);

        // 2. Buscar restaurantes reais publicados correspondentes no MariaDB
        var searchTerm = detectedCategory ?? (isFish ? "Peixe" : isTacaca ? "Tacacá" : isAcai ? "Açaí" : prompt);
        var searchResult = await restaurantRepo.SearchAsync(new RestaurantSearchQuery(
            Term: searchTerm,
            City: detectedCity,
            PageSize: 6,
            GuideOnly: true
        ), ct);
        var candidates = searchResult.Items.Take(4).ToList();

        var recommendations = candidates.Take(4).Select(r => new CuiaAiRecommendation(
            Id: r.Id,
            Name: r.Name,
            City: r.City,
            Neighborhood: r.Neighborhood,
            Category: r.Category,
            Address: r.Address,
            Phone: r.Phone,
            Reason: GenerateReasonForRestaurant(r, pLower),
            CuiaScore: null,
            PriceRange: null,
            PhotoUrl: r.PhotoUrl
        )).ToList();

        // 3. Buscar receitas regionais relacionadas
        var allRecipesResult = await recipesRepo.ListAsync(new RecipeFilter(), false, ct);
        var matchingRecipes = allRecipesResult.Items
            .Where(r =>
                (isTacaca && r.Slug.Contains("tacaca")) ||
                (isManicoba && r.Slug.Contains("manicoba")) ||
                (isFish && (r.Slug.Contains("peixe") || r.Slug.Contains("filhote") || r.Slug.Contains("moqueca"))) ||
                r.Title.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                r.Category.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .Select(r => new CuiaAiRecipeMatch(
                Slug: r.Slug,
                Title: r.Title,
                Subtitle: r.Subtitle,
                TotalMinutes: r.TotalMinutes,
                Difficulty: r.Difficulty
            )).ToList();

        // 4. Curiosidades e Dicas Culturais
        var curiosities = SelectCuriosities(pLower);
        var followUps = GenerateFollowUps(pLower, detectedCity);

        // 5. Geração da Resposta (Gemini 2.0 Flash com fallback local autônomo)
        string answer;
        string engineUsed = "Cuia Knowledge Engine (Amazônia)";

        if (_geminiProvider != null)
        {
            try
            {
                var systemPrompt = """
                    Você é o Sommelier e Concierge da Cuia IA do portal 'Belém Restaurantes — O Pará tem sabor'.
                    Sua personalidade é acolhedora, vibrante, poética e conhecedora íntima dos ingredientes e tradições paraenses (tucupi, jambu, chicória do Pará, aviú, filhote na brasa, maniçoba cozida 7 dias, açaí puro batido com farinha d'água de Bragança).
                    Use formatação Markdown elegante (títulos com ##, negrito, listas). Seja objetivo e empolgante.
                    Cite somente estabelecimentos da lista restaurantes_encontrados. Se a lista estiver vazia, diga que a camada Guia ainda não tem lugar conferido e aponte a busca por prato. Não invente horário de barco, tarifa, ponto de embarque nem prato que o cadastro não cita.
                    """;

                var contextPayload = JsonSerializer.Serialize(new
                {
                    pergunta = prompt,
                    cidade = detectedCity ?? "Belém",
                    restaurantes_encontrados = recommendations.Select(r => new { r.Name, r.City, r.Neighborhood, r.Category }),
                    receitas = matchingRecipes.Select(rc => rc.Title),
                    dieta = query.Dietary,
                    ocasiao = query.Occasion
                });

                var response = await _geminiProvider.CompleteAsync(new AiCompletionRequest(
                    SystemInstruction: systemPrompt,
                    UserPayload: $"Gere uma recomendação gastronômica sob medida para a seguinte consulta: {prompt}. Dados locais: {contextPayload}",
                    Operation: "cuia_concierge",
                    MaxOutputTokens: 600,
                    Temperature: 0.3
                ), ct);

                if (!string.IsNullOrWhiteSpace(response.RawContent) && response.IsValid)
                {
                    answer = response.RawContent.Trim();
                    engineUsed = $"Google Gemini ({_geminiProvider.Provider})";
                }
                else
                {
                    answer = GenerateAutonomousEditorialResponse(prompt, pLower, recommendations, matchingRecipes, detectedCity);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Gemini provider falhou na resposta do concierge. Acionando fallback autônomo Cuia Engine.");
                answer = GenerateAutonomousEditorialResponse(prompt, pLower, recommendations, matchingRecipes, detectedCity);
            }
        }
        else
        {
            answer = GenerateAutonomousEditorialResponse(prompt, pLower, recommendations, matchingRecipes, detectedCity);
        }

        return new CuiaAiResponse(
            AnswerMarkdown: answer,
            Recommendations: recommendations,
            Recipes: matchingRecipes,
            RegionalCuriosities: curiosities,
            SuggestedFollowUps: followUps,
            EngineUsed: engineUsed
        );
    }

    public async Task<string> GetRestaurantAdviceAsync(long restaurantId, CancellationToken ct)
    {
        var restaurant = await restaurantRepo.GetPublishedAsync(restaurantId, ct);
        if (restaurant == null) return "Este restaurante não está disponível no catálogo público no momento.";

        var sb = new StringBuilder();
        sb.AppendLine($"### O que o cadastro de {restaurant.Name} permite dizer");
        sb.AppendLine();
        sb.Append("Publicado em **").Append(restaurant.City).Append("**");
        if (!string.IsNullOrWhiteSpace(restaurant.Neighborhood))
            sb.Append(", no bairro **").Append(restaurant.Neighborhood).Append("**");
        sb.AppendLine(".");
        if (!string.IsNullOrWhiteSpace(restaurant.Category))
            sb.AppendLine($"A categoria cadastrada é **{restaurant.Category}**. Isso não confirma o cardápio desta casa.");
        sb.AppendLine(string.IsNullOrWhiteSpace(restaurant.Address)
            ? "Endereço não informado."
            : $"Endereço informado: {restaurant.Address}.");
        if (string.IsNullOrWhiteSpace(restaurant.Phone))
            sb.AppendLine("Telefone não informado.");
        if (string.IsNullOrWhiteSpace(restaurant.PhotoUrl))
            sb.AppendLine("Não há foto com licença nesta ficha.");
        sb.AppendLine("Horário, preço e travessia só entram com fonte vigente. Este texto não inventa prato nem hora.");
        if (restaurant.Category?.Contains("Tacacá", StringComparison.OrdinalIgnoreCase) == true)
            sb.AppendLine("Nota cultural, separada desta casa: o tacacá paraense leva tucupi, jambu e camarão. Isso não confirma que este endereço sirva o prato agora.");
        else if (restaurant.Category?.Contains("Açaí", StringComparison.OrdinalIgnoreCase) == true)
            sb.AppendLine("Nota cultural, separada desta casa: o açaí no Pará é o fruto batido, muitas vezes com farinha e peixe. Isso não confirma o preparo deste endereço.");
        sb.AppendLine("O caminho fica no Cuia Rotas. Quem responde pelo lugar reivindica a ficha para completar horário e cardápio.");

        return sb.ToString();
    }

    public async Task<string> GetRecipeSubstitutionsAsync(string recipeSlug, CancellationToken ct)
    {
        var recipe = await recipesRepo.GetAsync(recipeSlug, false, ct);
        if (recipe == null) return "Receita não encontrada para análise de adaptações.";

        var sb = new StringBuilder();
        sb.AppendLine($"### 🍃 Guia de Adaptação Cuia IA: {recipe.Title}");
        sb.AppendLine();
        sb.AppendLine("Se você não está em Belém ou no Pará, veja como manter a essência da receita com ingredientes acessíveis:");
        sb.AppendLine();

        if (recipeSlug.Contains("tacaca"))
        {
            sb.AppendLine("- **Tucupi:** Em grandes capitais, você encontra tucupi engarrafado em mercados municipais ou empórios do Norte. Certifique-se de ferver bem com dentes de alho amassados, chicória e pimenta-de-cheiro.");
            sb.AppendLine("- **Jambu:** Se não encontrar folhas frescas de jambu, procure em conservas a vácuo em lojas especializadas. Em último caso, rúcula selvagem com agrião dá o frescor amargo, embora sem o espilantol (que amortece a boca).");
            sb.AppendLine("- **Goma de tapioca:** Use polvilho azedo ou doce hidratado com água fria e cozido em fogo brando até virar um mingau elástico transparente.");
        }
        else if (recipeSlug.Contains("manicoba"))
        {
            sb.AppendLine("- **Maniva:** ATENÇÃO! A maniva moída PRECISA ser pré-cozida por pelo menos 7 dias para eliminar o ácido cianídrico. Fora do Pará, compre apenas a maniva que já venha cozida e congelada de fornecedores certificados.");
            sb.AppendLine("- **Defumados e carnes:** Use lombo salgado, costelinha suína defumada, paio, linguiça calabresa de boa qualidade e toucinho.");
        }
        else
        {
            sb.AppendLine("- **Chicória do Pará (Culantro):** Pode ser encontrada em feiras orgânicas ou substituída por uma combinação de coentro fresco com folhas de espinafre e raspas suaves de limão.");
            sb.AppendLine("- **Castanha-do-Pará:** Utilize castanhas frescas e crocantes; ralar sobre o prato final traz a textura amanteigada inconfundível.");
        }

        sb.AppendLine();
        sb.AppendLine("👨‍🍳 *Dica de ouro da Cuia IA: O segredo da culinária amazônica está no equilíbrio entre a acidez natural do tucupi e a pungência aromática da pimenta-de-cheiro!*");

        return sb.ToString();
    }

    private static string DetectCategory(string p)
    {
        if (p.Contains("tacacá") || p.Contains("tacaca")) return "Tacacá";
        if (p.Contains("açaí") || p.Contains("acai")) return "Açaí";
        if (p.Contains("peixe") || p.Contains("filhote") || p.Contains("pescado")) return "Peixaria";
        if (p.Contains("pizza") || p.Contains("pizzaria")) return "Pizzaria";
        if (p.Contains("hambúrguer") || p.Contains("burger")) return "Hamburgueria";
        if (p.Contains("café") || p.Contains("padaria")) return "Cafeteria";
        if (p.Contains("mercado") || p.Contains("feira")) return "Feira e mercado";
        return null!;
    }

    private static string DetectCity(string p)
    {
        string[] cities = ["Belém", "Ananindeua", "Castanhal", "Bragança", "Santarém", "Salinópolis", "Soure", "Salvaterra", "Barcarena", "Marabá", "Paragominas", "Cametá", "Vigia", "Abaetetuba"];
        foreach (var c in cities)
        {
            if (p.Contains(c.ToLowerInvariant())) return c;
        }
        return null!;
    }

    private static string GenerateReasonForRestaurant(RestaurantView r, string p)
    {
        var where = string.IsNullOrWhiteSpace(r.Neighborhood) ? r.City : $"{r.Neighborhood}, {r.City}";
        var category = string.IsNullOrWhiteSpace(r.Category) ? "Cadastro" : r.Category;
        return $"{category} na camada Guia, em {where}. Esta resposta não confirma horário, preço nem prato do dia.";
    }

    private static string GenerateAutonomousEditorialResponse(
        string prompt,
        string p,
        List<CuiaAiRecommendation> recs,
        List<CuiaAiRecipeMatch> recipes,
        string? city)
    {
        var sb = new StringBuilder();
        var cityName = city ?? "Belém";

        sb.AppendLine($"## 🌿 Recomendações da Cuia IA para você em {cityName}");
        sb.AppendLine();

        if (p.Contains("tacacá") || p.Contains("tacaca"))
        {
            sb.AppendLine("O **tacacá** é a maior joia da gastronomia de rua paraense. Servido fumegante na cuia, combina o caldo amarelo do tucupi fervido com chicória e alho, goma elástica de tapioca, camarões secos salgados e o inconfundível **jambu**, que adormece delicadamente os lábios.");
            sb.AppendLine();
            sb.AppendLine("Dica de etiqueta paraense: **não use colher**! O tacacá se bebe encostando a boca na borda da cuia e sorvendo o caldo; o palito de madeira serve apenas para fisgar as folhas de jambu e os camarões.");
        }
        else if (p.Contains("açaí") || p.Contains("acai"))
        {
            sb.AppendLine("O **açaí** no Pará é alimento sagrado e refeição substancial. O fruto é batido com água sem qualquer adição de açúcar, xaropes ou frutas, resultando em uma polpa densa, arroxeada e de sabor terroso singular.");
            sb.AppendLine();
            sb.AppendLine("A combinação clássica imbatível é a tigela de açaí acompanhada de **peixe frito bem crocante** (como pescada amarela ou filhote) e uma farta colherada de **farinha d'água de Bragança**.");
        }
        else if (p.Contains("combu") || p.Contains("rio") || p.Contains("barco"))
        {
            sb.AppendLine("A ida à **Ilha do Combu** é de barco. Este portal só mostra ponto, horário ou tarifa quando essa informação está vigente em [Cuia Rotas](/travessias). Até lá, confirme a travessia no dia.");
        }
        else if (p.Contains("vegetariano") || p.Contains("vegano"))
        {
            sb.AppendLine("Embora a mesa paraense celebre fartamente os pescados, as opções à base de plantas são fascinantes graças à exuberância botânica da floresta:");
            sb.AppendLine();
            sb.AppendLine("- **Arroz de Jambu e Castanha-do-Pará:** Um clássico aromático e reconfortante.");
            sb.AppendLine("- **Caldeirada de Cogumelos ou Pupunha no Tucupi:** Alternativa vegetal rica em umami amazônico.");
            sb.AppendLine("- **Açaí com tapioca e farinha d'água:** 100% vegano, energético e nutritivo.");
        }
        else
        {
            sb.AppendLine($"Para a sua busca sobre *\"{prompt}\"*, o Pará oferece sabores únicos que combinam técnicas milenares indígenas com influências ribeirinhas e contemporâneas.");
            sb.AppendLine();
            sb.AppendLine("Recomendamos começar pelos polos clássicos: a orla da **Estação das Docas** para alta gastronomia e vista da Baía do Guajará, os boxes do **Ver-o-Peso** para vivenciar o pulsar da cultura popular, e os charmosos bistrôs dos bairros **Nazaré** e **Umarizal**.");
        }

        if (recs.Count == 0)
        {
            sb.AppendLine();
            sb.AppendLine("A camada **Guia** ainda não tem um lugar conferido para este pedido. Uma ficha cadastral não é recomendação de visita. Veja os pratos em [Pratos do Pará](/pratos) e as travessias com fonte vigente em [Travessias](/travessias).");
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("### Lugares da camada Guia:");
            foreach (var r in recs)
            {
                var loc = !string.IsNullOrWhiteSpace(r.Neighborhood) ? $"{r.Neighborhood}, {r.City}" : r.City;
                sb.AppendLine($"- **{r.Name}** ({r.Category ?? "Restaurante"} · {loc}): {r.Reason}");
            }
        }

        if (recipes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### 📖 Do catálogo de receitas para preparar em casa:");
            foreach (var rc in recipes)
            {
                sb.AppendLine($"- [{rc.Title}](/receitas/{rc.Slug}) — *{rc.Subtitle}* (Preparo em {rc.TotalMinutes} min · Nível {rc.Difficulty})");
            }
        }

        return sb.ToString();
    }

    private static IReadOnlyList<string> SelectCuriosities(string p)
    {
        var list = new List<string>
        {
            "O jambu possui espilantol, uma substância bioativa que provoca uma leve e estimulante dormência na língua e estimula a salivação.",
            "A farinha d'água de Bragança é considerada patrimônio imaterial paraense por seu processo artesanal de fermentação em água e torragem em forno de barro.",
            "O tucupi é o sumo extraído da raiz da mandioca-brava. Como a raiz crua possui ácido cianídrico, o líquido precisa ser cozido por horas até tornar-se totalmente seguro e saboroso."
        };

        if (p.Contains("combu") || p.Contains("cacau"))
        {
            list.Insert(0, "A Ilha do Combu abriga plantações centenárias de cacau nativo da várzea amazônica, produzindo chocolates artesanais premiados internacionalmente.");
        }
        if (p.Contains("peixe") || p.Contains("filhote"))
        {
            list.Insert(0, "O filhote (piraíba jovem) pode ultrapassar centenas de quilos no estado adulto, sendo um dos maiores peixes de água doce do planeta.");
        }

        return list.Take(3).ToList();
    }

    private static IReadOnlyList<string> GenerateFollowUps(string p, string? city)
    {
        var c = city ?? "Belém";
        return
        [
            $"Onde encontrar o melhor tacacá tradicional em {c}?",
            $"Como fazer a travessia para a Ilha do Combu?",
            $"Quais pratos tradicionais experimentar além da maniçoba?",
            "Quais as melhores opções de sobremesas com frutas amazônicas?"
        ];
    }
}
