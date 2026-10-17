using System;
using System.Collections.Generic;
using System.Linq;

namespace BelemRestaurantes.Domain.Data;

public sealed record ParaMunicipalityRecord(
    uint IbgeCode,
    string Name,
    string NormalizedName,
    string Mesoregion,
    string Microregion,
    bool IsTouristPole,
    int PriorityLevel);

public static class ParaOfficialCatalog
{
    public const int TotalOfficialMunicipalities = 144;
    public const int ParaStatePrefix = 15;

    public static readonly IReadOnlyList<ParaMunicipalityRecord> Municipalities = new ParaMunicipalityRecord[]
    {
        new(1500107, "Abaetetuba", "abaetetuba", "Nordeste Paraense", "Cametá", false, 2),
        new(1500131, "Abel Figueiredo", "abel figueiredo", "Sudeste Paraense", "Paragominas", false, 2),
        new(1500206, "Acará", "acara", "Nordeste Paraense", "Tomé-Açu", false, 2),
        new(1500305, "Afuá", "afua", "Marajó", "Furos de Breves", false, 2),
        new(1500347, "Água Azul do Norte", "agua azul do norte", "Sudeste Paraense", "Parauapebas", false, 2),
        new(1500404, "Alenquer", "alenquer", "Baixo Amazonas", "Santarém", false, 2),
        new(1500503, "Almeirim", "almeirim", "Baixo Amazonas", "Almeirim", false, 2),
        new(1500602, "Altamira", "altamira", "Sudoeste Paraense", "Altamira", true, 1),
        new(1500701, "Anajás", "anajas", "Marajó", "Furos de Breves", false, 2),
        new(1500800, "Ananindeua", "ananindeua", "Metropolitana de Belém", "Belém", true, 1),
        new(1500859, "Anapu", "anapu", "Sudoeste Paraense", "Altamira", false, 2),
        new(1500909, "Augusto Corrêa", "augusto correa", "Nordeste Paraense", "Bragantina", false, 2),
        new(1500958, "Aurora do Pará", "aurora do para", "Nordeste Paraense", "Guamá", false, 2),
        new(1501006, "Aveiro", "aveiro", "Sudoeste Paraense", "Itaituba", false, 2),
        new(1501105, "Bagre", "bagre", "Marajó", "Portel", false, 2),
        new(1501204, "Baião", "baiao", "Nordeste Paraense", "Cametá", false, 2),
        new(1501253, "Bannach", "bannach", "Sudeste Paraense", "São Félix do Xingu", false, 2),
        new(1501303, "Barcarena", "barcarena", "Metropolitana de Belém", "Belém", true, 1),
        new(1501402, "Belém", "belem", "Metropolitana de Belém", "Belém", true, 1),
        new(1501451, "Belterra", "belterra", "Baixo Amazonas", "Santarém", false, 2),
        new(1501501, "Benevides", "benevides", "Metropolitana de Belém", "Belém", false, 1),
        new(1501576, "Bom Jesus do Tocantins", "bom jesus do tocantins", "Sudeste Paraense", "Paragominas", false, 2),
        new(1501600, "Bonito", "bonito", "Nordeste Paraense", "Bragantina", false, 2),
        new(1501709, "Bragança", "braganca", "Nordeste Paraense", "Bragantina", true, 1),
        new(1501725, "Brasil Novo", "brasil novo", "Sudoeste Paraense", "Altamira", false, 2),
        new(1501758, "Brejo Grande do Araguaia", "brejo grande do araguaia", "Sudeste Paraense", "Marabá", false, 2),
        new(1501782, "Breu Branco", "breu branco", "Sudeste Paraense", "Tucuruí", false, 2),
        new(1501808, "Breves", "breves", "Marajó", "Furos de Breves", false, 2),
        new(1501907, "Bujaru", "bujaru", "Metropolitana de Belém", "Castanhal", false, 1),
        new(1501956, "Cachoeira do Piriá", "cachoeira do piria", "Nordeste Paraense", "Guamá", false, 2),
        new(1502004, "Cachoeira do Arari", "cachoeira do arari", "Marajó", "Arari", false, 2),
        new(1502103, "Cametá", "cameta", "Nordeste Paraense", "Cametá", true, 1),
        new(1502152, "Canaã dos Carajás", "canaa dos carajas", "Sudeste Paraense", "Parauapebas", false, 2),
        new(1502202, "Capanema", "capanema", "Nordeste Paraense", "Bragantina", false, 2),
        new(1502301, "Capitão Poço", "capitao poco", "Nordeste Paraense", "Guamá", false, 2),
        new(1502400, "Castanhal", "castanhal", "Metropolitana de Belém", "Castanhal", true, 1),
        new(1502509, "Chaves", "chaves", "Marajó", "Arari", false, 2),
        new(1502608, "Colares", "colares", "Nordeste Paraense", "Salgado", false, 2),
        new(1502707, "Conceição do Araguaia", "conceicao do araguaia", "Sudeste Paraense", "Conceição do Araguaia", true, 1),
        new(1502756, "Concórdia do Pará", "concordia do para", "Nordeste Paraense", "Tomé-Açu", false, 2),
        new(1502764, "Cumaru do Norte", "cumaru do norte", "Sudeste Paraense", "São Félix do Xingu", false, 2),
        new(1502772, "Curionópolis", "curionopolis", "Sudeste Paraense", "Parauapebas", false, 2),
        new(1502806, "Curralinho", "curralinho", "Marajó", "Furos de Breves", false, 2),
        new(1502855, "Curuá", "curua", "Baixo Amazonas", "Santarém", false, 2),
        new(1502905, "Curuçá", "curuca", "Nordeste Paraense", "Salgado", false, 2),
        new(1502939, "Dom Eliseu", "dom eliseu", "Sudeste Paraense", "Paragominas", false, 2),
        new(1502954, "Eldorado do Carajás", "eldorado do carajas", "Sudeste Paraense", "Parauapebas", false, 2),
        new(1503002, "Faro", "faro", "Baixo Amazonas", "Óbidos", false, 2),
        new(1503044, "Floresta do Araguaia", "floresta do araguaia", "Sudeste Paraense", "Conceição do Araguaia", false, 2),
        new(1503077, "Garrafão do Norte", "garrafao do norte", "Nordeste Paraense", "Guamá", false, 2),
        new(1503093, "Goianésia do Pará", "goianesia do para", "Sudeste Paraense", "Paragominas", false, 2),
        new(1503101, "Gurupá", "gurupa", "Marajó", "Portel", false, 2),
        new(1503200, "Igarapé-Açu", "igarape acu", "Nordeste Paraense", "Bragantina", false, 2),
        new(1503309, "Igarapé-Miri", "igarape miri", "Nordeste Paraense", "Cametá", false, 2),
        new(1503408, "Inhangapi", "inhangapi", "Metropolitana de Belém", "Castanhal", false, 1),
        new(1503457, "Ipixuna do Pará", "ipixuna do para", "Nordeste Paraense", "Guamá", false, 2),
        new(1503507, "Irituia", "irituia", "Nordeste Paraense", "Guamá", false, 2),
        new(1503606, "Itaituba", "itaituba", "Sudoeste Paraense", "Itaituba", false, 2),
        new(1503705, "Itupiranga", "itupiranga", "Sudeste Paraense", "Tucuruí", false, 2),
        new(1503754, "Jacareacanga", "jacareacanga", "Sudoeste Paraense", "Itaituba", false, 2),
        new(1503804, "Jacundá", "jacunda", "Sudeste Paraense", "Tucuruí", false, 2),
        new(1503903, "Juruti", "juruti", "Baixo Amazonas", "Óbidos", false, 2),
        new(1504000, "Limoeiro do Ajuru", "limoeiro do ajuru", "Nordeste Paraense", "Cametá", false, 2),
        new(1504059, "Mãe do Rio", "mae do rio", "Nordeste Paraense", "Guamá", false, 2),
        new(1504109, "Magalhães Barata", "magalhaes barata", "Nordeste Paraense", "Salgado", false, 2),
        new(1504208, "Marabá", "maraba", "Sudeste Paraense", "Marabá", true, 1),
        new(1504307, "Maracanã", "maracana", "Nordeste Paraense", "Salgado", false, 2),
        new(1504406, "Marapanim", "marapanim", "Nordeste Paraense", "Salgado", false, 2),
        new(1504422, "Marituba", "marituba", "Metropolitana de Belém", "Belém", false, 1),
        new(1504455, "Medicilândia", "medicilandia", "Sudoeste Paraense", "Altamira", false, 2),
        new(1504505, "Melgaço", "melgaco", "Marajó", "Portel", false, 2),
        new(1504604, "Mocajuba", "mocajuba", "Nordeste Paraense", "Cametá", false, 2),
        new(1504703, "Moju", "moju", "Nordeste Paraense", "Tomé-Açu", false, 2),
        new(1504752, "Mojuí dos Campos", "mojui dos campos", "Baixo Amazonas", "Santarém", false, 2),
        new(1504802, "Monte Alegre", "monte alegre", "Baixo Amazonas", "Santarém", false, 2),
        new(1504901, "Muaná", "muana", "Marajó", "Arari", false, 2),
        new(1504950, "Nova Esperança do Piriá", "nova esperanca do piria", "Nordeste Paraense", "Guamá", false, 2),
        new(1504976, "Nova Ipixuna", "nova ipixuna", "Sudeste Paraense", "Tucuruí", false, 2),
        new(1505007, "Nova Timboteua", "nova timboteua", "Nordeste Paraense", "Bragantina", false, 2),
        new(1505031, "Novo Progresso", "novo progresso", "Sudoeste Paraense", "Itaituba", false, 2),
        new(1505064, "Novo Repartimento", "novo repartimento", "Sudeste Paraense", "Tucuruí", false, 2),
        new(1505106, "Óbidos", "obidos", "Baixo Amazonas", "Óbidos", true, 1),
        new(1505205, "Oeiras do Pará", "oeiras do para", "Nordeste Paraense", "Cametá", false, 2),
        new(1505304, "Oriximiná", "oriximina", "Baixo Amazonas", "Óbidos", false, 2),
        new(1505403, "Ourém", "ourem", "Nordeste Paraense", "Guamá", false, 2),
        new(1505437, "Ourilândia do Norte", "ourilandia do norte", "Sudeste Paraense", "São Félix do Xingu", false, 2),
        new(1505486, "Pacajá", "pacaja", "Sudoeste Paraense", "Altamira", false, 2),
        new(1505494, "Palestina do Pará", "palestina do para", "Sudeste Paraense", "Marabá", false, 2),
        new(1505502, "Paragominas", "paragominas", "Sudeste Paraense", "Paragominas", true, 1),
        new(1505536, "Parauapebas", "parauapebas", "Sudeste Paraense", "Parauapebas", true, 1),
        new(1505551, "Pau D''Arco", "pau darco", "Sudeste Paraense", "Redenção", false, 2),
        new(1505601, "Peixe-Boi", "peixe boi", "Nordeste Paraense", "Bragantina", false, 2),
        new(1505635, "Piçarra", "picarra", "Sudeste Paraense", "Redenção", false, 2),
        new(1505650, "Placas", "placas", "Baixo Amazonas", "Santarém", false, 2),
        new(1505700, "Ponta de Pedras", "ponta de pedras", "Marajó", "Arari", false, 2),
        new(1505809, "Portel", "portel", "Marajó", "Portel", false, 2),
        new(1505908, "Porto de Moz", "porto de moz", "Baixo Amazonas", "Almeirim", false, 2),
        new(1506005, "Prainha", "prainha", "Baixo Amazonas", "Santarém", false, 2),
        new(1506104, "Primavera", "primavera", "Nordeste Paraense", "Bragantina", false, 2),
        new(1506112, "Quatipuru", "quatipuru", "Nordeste Paraense", "Bragantina", false, 2),
        new(1506138, "Redenção", "redencao", "Sudeste Paraense", "Redenção", false, 2),
        new(1506161, "Rio Maria", "rio maria", "Sudeste Paraense", "Redenção", false, 2),
        new(1506187, "Rondon do Pará", "rondon do para", "Sudeste Paraense", "Paragominas", false, 2),
        new(1506195, "Rurópolis", "ruropolis", "Sudoeste Paraense", "Itaituba", false, 2),
        new(1506203, "Salinópolis", "salinopolis", "Nordeste Paraense", "Salgado", true, 1),
        new(1506302, "Salvaterra", "salvaterra", "Marajó", "Arari", true, 1),
        new(1506351, "Santa Bárbara do Pará", "santa barbara do para", "Metropolitana de Belém", "Belém", false, 1),
        new(1506401, "Santa Cruz do Arari", "santa cruz do arari", "Marajó", "Arari", false, 2),
        new(1506500, "Santa Izabel do Pará", "santa izabel do para", "Metropolitana de Belém", "Castanhal", false, 1),
        new(1506559, "Santa Luzia do Pará", "santa luzia do para", "Nordeste Paraense", "Guamá", false, 2),
        new(1506583, "Santa Maria das Barreiras", "santa maria das barreiras", "Sudeste Paraense", "Conceição do Araguaia", false, 2),
        new(1506609, "Santa Maria do Pará", "santa maria do para", "Nordeste Paraense", "Bragantina", false, 2),
        new(1506708, "Santana do Araguaia", "santana do araguaia", "Sudeste Paraense", "Conceição do Araguaia", false, 2),
        new(1506807, "Santarém", "santarem", "Baixo Amazonas", "Santarém", true, 1),
        new(1506906, "Santarém Novo", "santarem novo", "Nordeste Paraense", "Bragantina", false, 2),
        new(1507003, "Santo Antônio do Tauá", "santo antonio do taua", "Metropolitana de Belém", "Castanhal", false, 1),
        new(1507102, "São Caetano de Odivelas", "sao caetano de odivelas", "Nordeste Paraense", "Salgado", false, 2),
        new(1507151, "São Domingos do Araguaia", "sao domingos do araguaia", "Sudeste Paraense", "Marabá", false, 2),
        new(1507201, "São Domingos do Capim", "sao domingos do capim", "Nordeste Paraense", "Guamá", false, 2),
        new(1507300, "São Félix do Xingu", "sao felix do xingu", "Sudeste Paraense", "São Félix do Xingu", false, 2),
        new(1507409, "São Francisco do Pará", "sao francisco do para", "Nordeste Paraense", "Bragantina", false, 2),
        new(1507458, "São Geraldo do Araguaia", "sao geraldo do araguaia", "Sudeste Paraense", "Redenção", false, 2),
        new(1507466, "São João da Ponta", "sao joao da ponta", "Nordeste Paraense", "Salgado", false, 2),
        new(1507474, "São João de Pirabas", "sao joao de pirabas", "Nordeste Paraense", "Salgado", false, 2),
        new(1507508, "São João do Araguaia", "sao joao do araguaia", "Sudeste Paraense", "Marabá", false, 2),
        new(1507607, "São Miguel do Guamá", "sao miguel do guama", "Nordeste Paraense", "Guamá", false, 2),
        new(1507706, "São Sebastião da Boa Vista", "sao sebastiao da boa vista", "Marajó", "Furos de Breves", false, 2),
        new(1507755, "Sapucaia", "sapucaia", "Sudeste Paraense", "Redenção", false, 2),
        new(1507805, "Senador José Porfírio", "senador jose porfirio", "Sudoeste Paraense", "Altamira", false, 2),
        new(1507904, "Soure", "soure", "Marajó", "Arari", true, 1),
        new(1507953, "Tailândia", "tailandia", "Nordeste Paraense", "Tomé-Açu", false, 2),
        new(1507961, "Terra Alta", "terra alta", "Nordeste Paraense", "Salgado", false, 2),
        new(1507979, "Terra Santa", "terra santa", "Baixo Amazonas", "Óbidos", false, 2),
        new(1508001, "Tomé-Açu", "tome acu", "Nordeste Paraense", "Tomé-Açu", false, 2),
        new(1508035, "Tracuateua", "tracuateua", "Nordeste Paraense", "Bragantina", false, 2),
        new(1508050, "Trairão", "trairao", "Sudoeste Paraense", "Itaituba", false, 2),
        new(1508084, "Tucumã", "tucuma", "Sudeste Paraense", "São Félix do Xingu", false, 2),
        new(1508100, "Tucuruí", "tucurui", "Sudeste Paraense", "Tucuruí", false, 2),
        new(1508126, "Ulianópolis", "ulianopolis", "Sudeste Paraense", "Paragominas", false, 2),
        new(1508159, "Uruará", "uruara", "Sudoeste Paraense", "Altamira", false, 2),
        new(1508209, "Vigia", "vigia", "Nordeste Paraense", "Salgado", true, 1),
        new(1508308, "Viseu", "viseu", "Nordeste Paraense", "Guamá", false, 2),
        new(1508357, "Vitória do Xingu", "vitoria do xingu", "Sudoeste Paraense", "Altamira", false, 2),
        new(1508407, "Xinguara", "xinguara", "Sudeste Paraense", "Redenção", false, 2)
    };

    public static bool ValidateIbgeChecksum(uint ibgeCode)
    {
        var s = ibgeCode.ToString();
        if (s.Length != 7 || !s.StartsWith("15"))
        {
            return false;
        }

        int[] weights = [1, 2, 1, 2, 1, 2];
        int sum = 0;
        for (int i = 0; i < 6; i++)
        {
            int digit = s[i] - '0';
            int prod = digit * weights[i];
            if (prod >= 10)
            {
                sum += (prod / 10) + (prod % 10);
            }
            else
            {
                sum += prod;
            }
        }

        int check = (10 - (sum % 10)) % 10;
        int expected = s[6] - '0';
        return check == expected;
    }
}