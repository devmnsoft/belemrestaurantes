# Sprint 03 — relatório técnico (16/10/2026)

**Relatório histórico da entrega de estabilização (commit 29e26a0).** A evolução posterior de contas, e-mail e idiomas está em [MEMBERS-EMAIL-I18N.md](MEMBERS-EMAIL-I18N.md).

**Status nesta entrega histórica: sprint não homologada.** Os dois pedidos originais abrangem também módulos ainda ausentes. Este relatório não representa aceite integral nem validação da VPS.

## Preservado

Solution em camadas Domain, Application, Infrastructure, Web, Worker e Tests; catálogo público, busca e filtros, sugestões, moderação de restaurantes, deduplicação por Place ID, proveniência, favoritos, avaliações existentes, solicitações de exclusão, adaptadores de IA e fila de descoberta. Nenhum script 001–007 foi alterado. A Web continua sem serviço de descoberta.

## Correções implementadas

- Removidos endpoint e formulário públicos de bootstrap administrativo. Provisionamento passou para comando local interativo, com senha oculta e validação de política; não inicia servidor HTTP.
- Esquema de autenticação unificado no login/logout. MFA obrigatório nas políticas administrativas, inscrição corrigida, desafio limitado a cinco minutos, vínculo à versão da senha, segredo cifrado por Data Protection e consumo atômico do contador TOTP para impedir replay.
- Revalidação de cookies consulta conta e papéis a cada requisição; bloqueio, desativação, alteração de senha ou papéis invalidam sessão. Políticas negam acesso por padrão; catálogo, login e saúde são explicitamente públicos.
- Tentativas inválidas de senha também são contabilizadas; verificação de senha para conta inexistente executa PBKDF2 equivalente, reduzindo diferença de tempo. Novos hashes usam PBKDF2-SHA256 com 600 mil iterações; hashes antigos compatíveis continuam verificáveis. Hashes malformados e iterações abusivas são rejeitados.
- Auditoria de login bem-sucedido, falha de login, logout e provisionamento; ator e correlação propagados sem registrar senha ou token. Auditoria de publicação existente preservada.
- Rate limiting particionado por endereço remoto, limite administrativo aplicado ao fluxo de autenticação. Cookies HttpOnly/Secure/Strict preservados; CSRF global preservado; câmera permitida apenas na própria origem. Readiness omite descrição interna de erros. Hosts públicos permitidos restringidos.
- Migrações serializadas por GET_LOCK, histórico/checksums preservados e registro de tentativa antes do DDL. Tentativa interrompida impede reaplicação cega; recuperação exige reconciliação ou restauração pelo operador.
- Banco restrito a `belrestaurante`; conexão remota exige VerifyFull. Conexão falhada é descartada.
- Corrigidos OpenAsync duplicados em persistência, SQL inválido de retry, contagem de tentativas e precedência de filtros. Falha reportada pelo orquestrador deixa de ser marcada como sucesso pelo Worker.
- Worker reserva uma tarefa por vez dentro do lote limitado, usa timeout de três minutos para lease de quatro minutos e backoff exponencial. Alvo concluído é reagendado (padrão sete dias, configurável); alvo pausado não é reservado e dead-letter impede recriação automática indiscriminada. Enfileiramento serializado entre instâncias; reservas usam ReadCommitted e retry limitado de deadlock/timeout de lock.
- Corrigida materialização Dapper de tipos numéricos UNSIGNED e instantes UTC, além de layouts administrativos ausentes/incompletos. Corrigido caractere NUL literal no fonte de ingestão, mantendo seu valor lógico como escape C#.
- JWT provisório sem uso removido. Worker permanente não aplica migrações por padrão. O projeto Worker estava configurado como biblioteca; agora gera executável e apphost, necessários ao serviço.
- Perfil Folder Release e publicação separada Web/Worker. Instalador usa identidade virtual dedicada, configura recuperação e aguarda configuração antes de iniciar; desinstalador preserva dados e arquivos.
- Testes MySQL não retornam sucesso quando a conexão falta. Removido fallback para banco da aplicação; script reproduzível cria MySQL descartável no loopback e remove o container ao terminar.

## Migrações e compatibilidade

Adicionada `008_mfa_replay.sql`: contador TOTP em `admin_users`. O runner também cria `schema_migration_attempts` como infraestrutura do controle de aplicação. Não foram reescritos checksums nem migrações antigas.

Contas existentes com segredo TOTP anteriormente armazenado em texto puro precisam de reinscrição controlada; o novo código não aceita esse material como se estivesse cifrado. Preserve diretório de chaves e identidade Windows antes de publicar. O modo local de provisionamento cria contas novas; recuperação de MFA de contas antigas ainda exige procedimento operacional controlado.

## Verificação e evidências

Builds Debug e Release da Solution passaram sem warnings/erros. A publicação Folder da Web e win-x64 do Worker foi validada, incluindo web.config, executável e runtimeconfig do serviço. O executável Worker iniciou em console com descoberta/migrações desativadas e recebeu Ctrl+C para encerramento; isso não substitui o ensaio como Windows Service. O comando local --migrate foi executado com sucesso sobre o MySQL temporário. A consulta NuGet de pacotes diretos e transitivos não apontou vulnerabilidades conhecidas nas fontes configuradas; isso não substitui revisão de código.

Resultado final: **63 testes aprovados, zero falhas e zero ignorados** — 46 unitários e 17 de integração, incluindo MySQL 8.4 descartável em banco vazio. Evidências finais: `artifacts/test-results/sprint03_net10.0_20261016170608.trx` (46 unitários) e `artifacts/test-results/sprint03_net10.0_20261016170623.trx` (17 integração). Execuções anteriores, inclusive falhas corrigidas, também foram preservadas nesse diretório local. Os testes de integração cobrem migrações em banco vazio e idempotência, runners concorrentes, reserva concorrente de fila sem duplicação, retry/dead-letter/recuperação de lease, login com senha + TOTP + CSRF, replay de TOTP, negação a anunciante, revogação após mudança de senha, endpoints protegidos e segurança básica HTTP.

`CatalogJourneyTests` usa **fixture identificada como test_fixture**, grava MySQL real, repete Place ID, confirma invisibilidade enquanto pendente, aprova, consulta catálogo publicado e verifica auditoria. **Não é uma consulta real ao Google.** O teste de login usa conta temporária e segredo TOTP gerado no teste, sem credencial administrativa fixa. O teste de recuperação de lease simula a persistência de trabalho interrompido; não comprova reinício real do Windows Service.

Reprodução: `pwsh -File scripts/test-sprint03.ps1`. Artefatos de publicação ficam em `artifacts/publish/web` e `artifacts/publish/worker`, fora do Git. Não houve publicação na VPS.

## Pendências impeditivas do aceite integral

| Área | Trabalho ainda necessário |
| --- | --- |
| Contas públicas/comerciantes | Cadastro responsivo, confirmação de e-mail, login próprio, recuperação por token único, dispositivos/sessões, troca de e-mail/senha, exportação e exclusão efetivas; isolamento de propriedade e testes IDOR completos. |
| Minha Experiência | Modelo e migrações de experiências públicas/privadas, CRUD do proprietário, múltiplas fotos, pratos/valores, curtidas/salvos/denúncias, feed, conta pessoal e notificações. As avaliações simples existentes não equivalem ao módulo solicitado. |
| Upload | Validação por conteúdo, reprocessamento/EXIF, malware, armazenamento privado, autorização para servir imagens, thumbnails/progresso e testes reais em celular. Nenhum upload foi declarado concluído. |
| E-mail Locaweb | Adaptador SMTP, templates, fila durável, retries/limites, confirmação e recuperação de conta; teste autorizado com configurações reais. SPF/DKIM/DMARC e painel do provedor não foram alterados. |
| Privacidade e comunidade | Regras completas de visibilidade no backend, compartilhamento WhatsApp/OG, menores, moderação humana de fotos/relatos/denúncias e proteção de dados pessoais. CPF não foi coletado nem tornado obrigatório. |
| Selo Cuia | Ranking apenas de experiências públicas aprovadas, reputação/antifraude, diferenciação explícita de avaliações externas e independência de patrocínio. |
| Descoberta Google | Controle durável e atômico de orçamento/quota Places, revisão das regras de retenção/atribuição, campos de detalhes e paginação, confronto de alterações com conteúdo aprovado e teste externo autorizado. Não ativar em produção antes dessas etapas. |
| Painel administrativo | Dashboard operacional completo de fontes, duplicatas, correção, filas, reprocessamento, consumo, usuários, denúncias, e-mails e exclusões; há somente os fluxos administrativos básicos. |
| IA | Adaptadores existentes preservados; revisão completa de prompt injection, limites/custos, validação de saída e testes reais dos três provedores ainda necessária. |
| MySQL e operação | Aplicação dos usuários/privilégios mínimos, retenção, backup automático cifrado, ensaio de restauração e recuperação de falha parcial; regressões contra banco existente representativo. |
| Segurança residual | Auditoria de todas as ações/gestão de papéis, bloqueio progressivo completo, recuperação de MFA, revisão integral XSS/SSRF/uploads/mass assignment, CSP sem unsafe-inline e verificação completa de logs/segredos. |
| Homologação Windows | Instalação real do serviço com ACL/segredos, Event Log/monitoramento, interrupção/reinício, HTTPS/certificados/HSTS, jornada em celular e rollback ensaiado. |

## Dependências externas e critério final

Não foram realizadas chamadas pagas ao Google/IA, envio SMTP, alterações de DNS, instalação do serviço nesta máquina ou deploy na VPS. As integrações externas precisam de configuração autorizada e ambiente de homologação. Há também trabalho interno pendente, listado acima; não é correto atribuir toda a incompletude a credenciais externas.

A jornada **Windows Service → Google real → MySQL → aprovação com MFA → site de homologação → auditoria** não foi homologada. A jornada **cadastro → confirmação → experiência com foto/privacidade → compartilhamento → edição/exclusão** não foi implementada integralmente.

Procedimentos de publicação manual, privilégios, manutenção, reinscrição TOTP e rollback: [deploy/windows/README.md](../deploy/windows/README.md).

## Arquivos

As alterações abrangem README/documentação Windows; scripts de teste, instalação, publicação e desinstalação; contratos/modelos de Application; autenticação, DI, migrações e repositórios MySQL; controllers, layouts, configuração e perfil Folder da Web; ciclo/configuração do Worker; testes de autenticação, segurança HTTP, catálogo e concorrência. A relação exata está no diff e no commit da entrega (`git show --stat`).

```text
.gitignore
README.md
deploy/windows/README.md
docs/SPRINT03-REPORT.md
scripts/test-sprint03.ps1
scripts/windows/install-service.ps1
scripts/windows/publish.ps1
scripts/windows/uninstall-service.ps1
src/BelemRestaurantes.Application/Abstractions/IAdminUserRepository.cs
src/BelemRestaurantes.Application/Models/RestaurantView.cs
src/BelemRestaurantes.Infrastructure/InfrastructureAuth.cs
src/BelemRestaurantes.Infrastructure/InfrastructureServiceCollectionExtensions.cs
src/BelemRestaurantes.Infrastructure/Migrations/MigrationRunner.cs
src/BelemRestaurantes.Infrastructure/Migrations/Scripts/008_mfa_replay.sql
src/BelemRestaurantes.Infrastructure/MySql/MySqlAdminUserRepository.cs
src/BelemRestaurantes.Infrastructure/MySql/MySqlCommunityRepositories.cs
src/BelemRestaurantes.Infrastructure/MySql/MySqlConnectionFactory.cs
src/BelemRestaurantes.Infrastructure/MySql/MySqlCostLedger.cs
src/BelemRestaurantes.Infrastructure/MySql/MySqlDiscoveryQueue.cs
src/BelemRestaurantes.Infrastructure/MySql/MySqlDiscoveryTargetRepository.cs
src/BelemRestaurantes.Infrastructure/MySql/MySqlModerationRepository.cs
src/BelemRestaurantes.Infrastructure/MySql/MySqlRestaurantIngestionRepository.cs
src/BelemRestaurantes.Infrastructure/MySql/UtcDateTimeOffsetHandler.cs
src/BelemRestaurantes.Web/AdminMaintenance.cs
src/BelemRestaurantes.Web/BelemRestaurantes.Web.csproj
src/BelemRestaurantes.Web/Controllers/AdminAccountController.cs
src/BelemRestaurantes.Web/Controllers/EstablishmentController.cs
src/BelemRestaurantes.Web/Controllers/HomeController.cs
src/BelemRestaurantes.Web/Controllers/RestaurantsController.cs
src/BelemRestaurantes.Web/Program.cs
src/BelemRestaurantes.Web/Properties/PublishProfiles/FolderProfile.pubxml
src/BelemRestaurantes.Web/Views/AdminAccount/Login.cshtml
src/BelemRestaurantes.Web/Views/AdminAccount/VerifyMfa.cshtml
src/BelemRestaurantes.Web/Views/Shared/_AdminLayout.cshtml
src/BelemRestaurantes.Web/Views/Shared/_Layout.cshtml
src/BelemRestaurantes.Web/appsettings.json
src/BelemRestaurantes.Worker/BelemRestaurantes.Worker.csproj
src/BelemRestaurantes.Worker/DiscoveryHostedService.cs
src/BelemRestaurantes.Worker/Program.cs
tests/BelemRestaurantes.IntegrationTests/AdminLoginTests.cs
tests/BelemRestaurantes.IntegrationTests/BelemRestaurantes.IntegrationTests.csproj
tests/BelemRestaurantes.IntegrationTests/CatalogJourneyTests.cs
tests/BelemRestaurantes.IntegrationTests/DatabaseEnvironment.cs
tests/BelemRestaurantes.IntegrationTests/MigrationTests.cs
tests/BelemRestaurantes.IntegrationTests/QueueConcurrencyTests.cs
tests/BelemRestaurantes.IntegrationTests/SecurityEndpointTests.cs
tests/BelemRestaurantes.IntegrationTests/WebEndpointTests.cs
tests/BelemRestaurantes.UnitTests/AuthenticationSecurityTests.cs
```
