# Belém Restaurantes

Plataforma gastronômica do Pará em ASP.NET Core 10, MVC/Razor, JavaScript e MySQL `belrestaurante`. Ambiente alvo: VPS Windows + IIS; publicação manual pelo Visual Studio 2026; descoberta em Windows Service independente.

## Desenvolvimento e manutenção

1. Configure `ConnectionStrings__MySql` em segredo do processo. Banco remoto exige `SslMode=VerifyFull` e certificado confiável; não use credenciais de produção nos testes.
2. Execute `dotnet restore` e `dotnet build BelemRestaurantes.sln`.
3. Aplique migrações com credencial própria: `dotnet run --project src/BelemRestaurantes.Web -- --migrate`. Não aplique os SQL legados de `sql/`.
4. Crie o primeiro administrador em console local interativo: `dotnet run --project src/BelemRestaurantes.Web -- --provision-admin`. Complete o TOTP no primeiro login. Não existe bootstrap HTTP.
5. Inicie Web e verifique `/health` e `/health/ready`. A Web não executa descoberta.
6. O Worker mantém migrações e descoberta desativadas por padrão. Ative descoberta somente após homologar Google Places, quotas, custos e retenção. Chaves de APIs ficam em segredos de ambiente.

## Verificação

- Testes unitários: `dotnet test tests/BelemRestaurantes.UnitTests`.
- Suite completa em MySQL descartável, acessível apenas no loopback: `pwsh -File scripts/test-sprint03.ps1` (Docker necessário). O script cria banco `belrestaurante` isolado, executa a suite e remove o container.
- Para MySQL de homologação dedicado, defina `BELRESTAURANTES_TEST_MYSQL`; os testes de banco falham sem essa configuração. Não há fallback para a conexão de produção.
- Publicação Folder: `dotnet publish src/BelemRestaurantes.Web -c Release /p:PublishProfile=FolderProfile`.

Consulte [publicação Windows](deploy/windows/README.md) e o [relatório da Sprint 03](docs/SPRINT03-REPORT.md). Consulte também [contas, e-mail e oito idiomas](docs/MEMBERS-EMAIL-I18N.md). Cadastro público, confirmação, recuperação e fila de e-mail foram adicionados; configure o keyring privado e o SMTP do Worker para entrega real. A sprint ainda não está homologada: experiências com fotos, integrações externas e VPS continuam pendentes.
