using System.Text;
using System.Text.Json;
using BelemRestaurantes.Application.Services;
using BelemRestaurantes.Domain.Entities;

namespace BelemRestaurantes.UnitTests;

public sealed class Sprint49BudgetTests
{
    [Fact]
    public void FourShortTurnsFitExistingBudgetWithoutCountingTransportEscapes()
    {
        var request = new AiCompletionRequest(string.Concat(Enumerable.Repeat("Você é a Cuia, orientação de gastronomia no Pará. ",20)),"{\"pergunta\":\"Opção econômica em Belém?\"}","test",800,0.2,true);
        var estimate=CuiaSessionBudget.Estimate(request);
        Assert.True(estimate >= Encoding.UTF8.GetByteCount(request.SystemInstruction+request.UserPayload)+800);
        Assert.True(estimate*4<=24000);
        Assert.True(estimate<Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(request))+800+256);
    }
    [Theory]
    [InlineData("E como posso confirmar o cardápio atual?",true)]
    [InlineData("Como confirmar o cardápio do Outro Restaurante?",false)]
    public void MenuContinuationDoesNotReplaceAnExplicitNewPlace(string question,bool expected)=>Assert.Equal(expected,CuiaAnswerRules.IsMenuContinuation(question));
}
