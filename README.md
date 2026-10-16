# Belém Restaurantes
Plataforma gastronômica do Pará, ASP.NET Core MVC/Razor + JavaScript + MySQL.

## Ambiente
Domínio belemrestaurantes.com.br, Locaweb Hospedagem I Linux, banco MySQL belrestaurante.

O plano compartilhado não foi confirmado como compatível com processos persistentes ASP.NET Core. Para frontend dinâmico e worker contínuo, usar hospedagem com suporte a .NET (VPS/container). Aproveitar MySQL da Locaweb apenas se o acesso seguro externo for suportado.

## Configuração
1. Configurar ConnectionStrings__MySql e GOOGLE_PLACES_API_KEY em segredos de ambiente.
2. Executar dotnet restore e dotnet build.
3. Iniciar o Worker (BelemRestaurantes.Worker): ele aplica as migrações MySQL embutidas em src/BelemRestaurantes.Infrastructure/Migrations/Scripts no startup (Database:ApplyMigrations=true por padrão). Não aplicar schemas manuais — os arquivos em sql/ são legados e divergem das migrações oficiais.
4. Iniciar a Web com Discovery__Enabled=false, validar /health/ready e orçamento da API.
5. Habilitar worker e verificar moderação: novos registros ficam pending.

Leia docs/LOCAWEB.md, docs/SECURITY.md e docs/INTEGRATIONS.md.
Fotos externas, avaliações e integração de redes sociais dependem de permissões e conectores não implementados.