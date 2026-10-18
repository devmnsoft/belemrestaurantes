using System.Text.Json;
using BelemRestaurantes.Infrastructure.OpenData;

namespace BelemRestaurantes.UnitTests;

public sealed class FoodRegistryTests
{
    private static Dictionary<string,object?> Item() => new()
    {
        ["cnpj"]="11222333000181",["nome_fantasia"]="Sabores da Vila",["uf"]="PA",["situacao_cadastral"]=2,
        ["cnae_fiscal"]=5611201,["municipio"]="BELEM",["codigo_municipio_ibge"]=1501402,
        ["logradouro"]="DA FEIRA",["descricao_tipo_de_logradouro"]="RUA",["numero"]="10",["bairro"]="CAMPINA",
        ["ddd_telefone_1"]="9133334444",["razao_social"]="NOME CIVIL NÃO IMPORTAR",
        ["qsa"]=new[]{new{nome_socio="SOCIO NÃO IMPORTAR",cpf="12345678900"}},["email"]="nao-importar@example.test"
    };
    private static string Page(params Dictionary<string,object?>[] items) => JsonSerializer.Serialize(new{fetchedAt=DateTimeOffset.UtcNow,data=items});

    [Fact]
    public void OnlyBusinessProjectionIsImported_WithStableIdAndNoInventedCoordinates()
    {
        var batch=FoodRegistryParser.ParsePage(Page(Item()));
        var place=Assert.Single(batch.Places);
        Assert.Equal("cnpj:11222333000181",place.ExternalId);Assert.Equal("Belém",place.City);
        Assert.Equal("RUA DA FEIRA, 10",place.Address);Assert.Equal("Campina",place.Neighborhood);
        Assert.Null(place.Latitude);Assert.Null(place.Longitude);Assert.Null(place.GoogleRating);Assert.Null(place.BusinessStatus);
        var serialized=JsonSerializer.Serialize(place,new JsonSerializerOptions{Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping});
        Assert.DoesNotContain("NÃO IMPORTAR",serialized);Assert.DoesNotContain("nao-importar@example.test",serialized);
        Assert.DoesNotContain("12345678900",serialized);Assert.Equal("https://minhareceita.org/11222333000181",place.GoogleMapsUri);
    }

    [Theory]
    [InlineData("uf","SP")]
    [InlineData("situacao_cadastral",8)]
    [InlineData("situacao_cadastral",null)]
    [InlineData("cnae_fiscal",6201501)]
    [InlineData("codigo_municipio_ibge",3550308)]
    [InlineData("municipio","Ananindeua")]
    [InlineData("nome_fantasia","")]
    [InlineData("nome_fantasia","12345678900 Fulano")]
    [InlineData("nome_fantasia","SEM NOME FANTASIA")]
    [InlineData("cnpj","11222333000182")]
    public void InvalidOrOutOfScopeRecordsAreExcluded(string field,object? value)
    {
        var item=Item();item[field]=value;
        Assert.Empty(FoodRegistryParser.ParsePage(Page(item)).Places);
    }

    [Theory]
    [InlineData("Pizza da Vila",5611201,"Pizzaria")]
    [InlineData("Açaí da Vila",5611203,"Açaí")]
    [InlineData("Café da Vila",5611203,"Cafeteria")]
    [InlineData("Pães da Vila",1091102,"Padaria")]
    [InlineData("Lanche da Vila",5611203,"Lanchonete")]
    public void CategoryUsesFoodActivityAndExplicitTradeName(string name,int cnae,string category)
    {
        var item=Item();item["nome_fantasia"]=name;item["cnae_fiscal"]=cnae;
        Assert.Equal(category,Assert.Single(FoodRegistryParser.ParsePage(Page(item)).Places).Category);
    }

    [Fact]
    public void MissingStreetDoesNotBecomeAnInventedAddress_AndRepeatedCnpjIsNotDuplicated()
    {
        var item=Item();item["logradouro"]=null;
        Assert.Null(Assert.Single(FoodRegistryParser.ParsePage(Page(item,item)).Places).Address);
        Assert.Throws<InvalidOperationException>(()=>FoodRegistryParser.ParsePage("{}"));
        Assert.Throws<InvalidOperationException>(()=>FoodRegistryParser.ParsePage(Page(Enumerable.Range(0,1025).Select(_=>Item()).ToArray())));
    }

    [Theory]
    [InlineData("00000000000000",false)]
    [InlineData("11222333000181",true)]
    [InlineData("04252011000110",true)]
    [InlineData("11222333000182",false)]
    public void CnpjCheckDigitsAreValidated(string number,bool expected)=>Assert.Equal(expected,FoodRegistryParser.IsValidCnpj(number));
}
