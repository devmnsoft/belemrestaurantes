# Belém Restaurantes
Plataforma gastronômica do Pará, ASP.NET Core MVC/Razor + JavaScript + MySQL.

## Ambiente
Domínio belemrestaurantes.com.br, Locaweb Hospedagem I Linux, banco MySQL belrestaurante.

O plano compartilhado não foi confirmado como compatível com processos persistentes ASP.NET Core. Para frontend dinâmico e worker contínuo, usar hospedagem com suporte a .NET (VPS/container). Aproveitar MySQL da Locaweb apenas se o acesso seguro externo for suportado.

## Configuração
1. Fazer backup e aplicar sql/mysql/001_schema.sql.
2. Configurar ConnectionStrings__MySql e GOOGLE_PLACES_API_KEY em segredos de ambiente.
3. Executar dotnet restore e dotnet build.
4. Iniciar com Discovery__Enabled=false, validar /health/ready e orçamento da API.
5. Habilitar worker e verificar moderação: novos registros ficam pending.

Leia docs/LOCAWEB.md, docs/SECURITY.md e docs/INTEGRATIONS.md.
Fotos externas, avaliações e integração de redes sociais dependem de permissões e conectores não implementados.