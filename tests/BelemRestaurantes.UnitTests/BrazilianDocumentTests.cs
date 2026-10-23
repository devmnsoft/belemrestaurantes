using BelemRestaurantes.Domain.Identity;
namespace BelemRestaurantes.UnitTests;
public sealed class BrazilianDocumentTests
{
    [Theory]
    [InlineData("529.982.247-25",true)] [InlineData("11111111111",false)]
    [InlineData("52998224726",false)] [InlineData("５２９９８２２４７２５",false)] [InlineData("529x98224725",false)]
    public void CpfValidationChecksDigitsAndRejectsInvalidCharacters(string value,bool valid)=>Assert.Equal(valid,BrazilianDocument.IsCpf(value));
    [Theory]
    [InlineData("18.160.057/0001-13",true)] [InlineData("12.ABC.345/01DE-35",true)]
    [InlineData("00.000.000/E08G-12",true)] [InlineData("12.abc.345/01de-35",true)]
    [InlineData("12.ABC.345/01DE-36",false)] [InlineData("00000000000000",false)] [InlineData("12.ABC.345/01DE-3X",false)]
    public void CnpjAcceptsNumericAndOfficialAlphanumericExamples(string value,bool valid)=>Assert.Equal(valid,BrazilianDocument.IsCnpj(value));
}
