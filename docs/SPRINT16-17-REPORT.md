# Sprints 16 e 17 — operação e planejamento de visitas

Implementação no repositório ASP.NET Core existente, validada em 18/10/2026. Entrega funcional dos fluxos descritos abaixo; não representa aceite integral de todas as integrações externas das duas sprints.

## Funcionalidades implementadas

- `/admin/moderacao`: filtros, paginação SQL, seleção entre páginas, confirmação com fotografia dos IDs e validade de 15 minutos. Aprovar/publicar, rejeitar com motivo, solicitar revisão, marcar possível duplicado e enfileirar enriquecimento. Lotes limitados por `Moderation:BatchLimit` (500 por padrão; máximo 1.000), transação, bloqueio ordenado, auditoria individual e repetição idempotente. Revalida dados na confirmação; informa aplicados e ignorados por cadastro.
- `/admin/operacao`: heartbeat com validade de 90 segundos, memória, estado das filas, paginação e filtro dos alvos. Permite cadastrar alvos para qualquer um dos 144 municípios oficiais, com fonte, prioridade, frequência e limite. Sincronização enfileira somente trabalho elegível; os botões antigos de diagnóstico também foram corrigidos para não consultar fontes na requisição nem reabrir orçamentos pausados. Agendador de um minuto e frequência por alvo são independentes.
- Worker: heartbeat a cada 20 segundos, logs NDJSON locais por instância/dia (limite 20 MB), desligamento ordenado e suporte ao Windows Service já existente. Recuperação de leases na fila de enriquecimento e correção da consulta de propriedade que referenciava uma coluna inexistente.
- Contas reais `superadmin.mnsoft`, `operador.teste` e `editor.teste` nas tabelas oficiais. Senhas iniciais aleatórias, somente hashes no banco, troca obrigatória, MFA obrigatório. Segredos iniciais locais protegidos por DPAPI do usuário Windows; nenhum segredo neste relatório ou no Git.
- Primeiro acesso usa desafio MFA em cookie cifrado, HttpOnly, Secure, SameSite Strict, válido por cinco minutos e vinculado à versão da senha. Corrigida perda de contexto no fluxo anterior. A troca inicial é exigida também nas APIs administrativas; papéis, bloqueios e versão da senha são revalidados na sessão.
- `/admin/usuarios`: gestão de papéis e bloqueio pelo administrador global; impede remover seu próprio acesso global e deixar o sistema sem administrador ativo.
- `/admin/editorial` e `/sabores`: editor pode salvar rascunhos e publicar textos com fonte; controle de versão concorrente, texto sem HTML e auditoria. Não recebe privilégios de moderação/operação.
- `/cuia-rotas` e `/qual-onibus`: destino do catálogo publicado, origem opcional por endereço/referência ou localização consentida, data, chegada/saída, modais e preferências. Planos salvos no navegador, remoção e compartilhamento sem origem. Mapa do destino OpenStreetMap carregado somente após clique, com atribuição.
- Funcionamento usa as tabelas existentes de horários do Portal Parceiro, com múltiplos turnos, madrugada, 24 horas, exceções, fechamento temporário, reserva, fonte, confirmação e expiração. Informação do parceiro tem precedência. Histórico e controle de versão na mesma transação. Sem informação válida, exibe “Horário não confirmado”.
- Provedor intercambiável `IMobilityProvider`; Google Routes com máscara explícita, alternativas, instruções e pontos fornecidos pela API, limite de caminhada e contador diário atômico. Não armazena origem ou conteúdo das rotas em cache persistente. Sem credencial/orçamento habilitados, oferece o planejador oficial Google Maps.
- Referências fluviais e municipais oficiais; não cria linhas, pontos, horários, tarifas, filas ou popularidade fictícios. “Melhor momento” declara indisponibilidade de histórico.
- Páginas públicas `/privacidade` e `/termos`, com explicação do tratamento da origem, planos locais e provedores externos. Atribuição e separação visual das informações de Google Maps e OpenStreetMap.

## Acesso às credenciais locais

Execute pessoalmente em um terminal privado da mesma conta Windows:

```powershell
.\scripts\windows\show-homologation-credentials.ps1
```

O arquivo cifrado fica em `%LOCALAPPDATA%\MNSOFT\BelemRestaurantes\homologacao.credentials.dpapi`. Não redirecione a saída do script para logs. O primeiro acesso exige cadastro de TOTP e troca da senha inicial. Os testes completos de MFA utilizam contas temporárias reais e removidas ao final; o TOTP das três contas solicitadas permanece para cadastro pelo operador.

Provisionamento idempotente, somente Windows/Development explicitamente autorizado:

```powershell
dotnet run --project src/BelemRestaurantes.Worker -c Release --no-build -- --environment Development --provision-homologation --authorize-homologation
```

`--reset-homologation` redefine as senhas dessas três contas; preserva MFA e exige nova troca. Não cria um segundo sistema de identidade.

## Configuração operacional

Migrações novas 021–023; scripts anteriores preservados. Serviço Windows `BelemRestauranteCuiaDiscovery`, instalação existente em `scripts/windows/install-service.ps1`, com recuperação e identidade dedicada. Instalação em produção depende da administração do host e não é executada implicitamente no computador de desenvolvimento.

Configure `Worker:LogDirectory` em diretório gravável pela identidade do serviço, fora do IIS, com política de retenção do operador. O padrão local é `%LOCALAPPDATA%\MNSOFT\BelemRestaurantes\logs`. O Worker não depende do Visual Studio. Evidência operacional sem segredos: `--verify-operations`; catálogo e ciclos: `--verify-discovery`.

Google Routes requer `GoogleRoutes:Enabled=true`, `GoogleRoutes:ApiKey` válida em User Secrets e `GoogleRoutes:DailyRequestLimit` explícito. O limite é por requisições; não é cotação em dinheiro. Para limite financeiro, configure também orçamento/cotas do projeto Google Cloud. A chave não é enviada ao navegador.

## Limitações que impedem declarar aceite integral

- Places API (New) ainda precisa de chave válida; a credencial OAuth existente não atende à API. A descoberta real por OpenStreetMap e o catálogo cadastral já foram comprovados na Sprint 15, sem substituir essa pendência específica do Google.
- As credenciais anteriormente expostas no histórico precisam ser revogadas/rotacionadas pelo titular nos respectivos provedores. Valores atuais não foram incluídos nos novos arquivos ou commits; remover do arquivo de configuração não revoga uma chave antiga. O histórico não foi reescrito.
- Google Routes não teve consulta externa homologada com chave válida. Os parsers e estados de indisponibilidade são testados; o link oficial do Maps funciona sem chave. O Maps exige reconferir data/horário; esses campos não são prometidos como parâmetros universais do link.
- Não foi fornecido feed GTFS/GTFS-Realtime oficial com autorização. Cadastro de referência não é importação nem sincronização GTFS. Não há motor GTFS, integração de reservas/lotação, estimativa histórica de melhor momento ou cálculo multimodal fluvial confirmado.
- O mapa interno mostra o destino; não desenha trajetos de ônibus. Geometria recebida de Google não é reaproveitada sobre cartografia de outro provedor. Roteiros possuem acesso ao planejamento por parada; ainda não há validação automática integral de conexões entre múltiplas paradas.
- Tarifas e horários exigem confirmação de fonte. Cadastro empresarial ativo e publicação no guia não atestam operação, qualidade ou acessibilidade.
- A identidade visual existente foi preservada e os novos componentes usam seus tokens. A migração completa de todos os CSS legados e auditoria WCAG integral de todas as telas permanecem além da verificação automatizada descrita nas evidências.
- Novas páginas operacionais e de mobilidade estão em português; o bloco público da homepage mantém traduções PT/EN/ES. Não se afirma tradução integral das novas jornadas para oito idiomas.

## Fontes consultadas

- [Google Maps URLs](https://developers.google.com/maps/documentation/urls/get-started): links universais e modais sem chave de API.
- [Google Routes computeRoutes](https://developers.google.com/maps/documentation/routes/reference/rest/v2/TopLevel/computeRoutes): campos, horários e instruções do provedor.
- [Google Routes — transporte público](https://developers.google.com/maps/documentation/routes/transit-route).
- [Google Routes — políticas e atribuição](https://developers.google.com/maps/documentation/routes/policies).
- [OpenStreetMap — exportação e incorporação](https://wiki.openstreetmap.org/wiki/Export).
- [SEGBEL — serviços municipais](https://seguranca.belem.pa.gov.br/servicos-semob/).
- [Agência Belém — transporte fluvial](https://agencia.belem.pa.gov.br/confira-os-horarios-do-geladao-fluvial-para-mosqueiro-e-cotijuba/). Referência histórica, sem importar horários como atuais.

## Verificações finais

Build Release: **zero avisos e zero erros**. Migrações 021–023 aplicadas e reaplicação idempotente verificada, sem alterar scripts anteriores ou recriar o banco.

- **213 testes unitários aprovados**: parsing de turnos, intervalos, madrugada, exceções, fechamento temporário, reserva, estados desconhecidos, links sem origem e respostas de transporte sem tarifa inventada.
- **87 testes de integração aprovados**, na seleção que inclui migrações, localização, endpoints, segurança, ingestão, moderação, proximidade e novas jornadas. Novos testes verificam seleção fixa, revalidação após alterações, isolamento por ator, auditoria, idempotência, publicação real, precedência do parceiro, histórico e versão dos horários, tarifas vencidas, criação de alvo oficial e rejeição de município inexistente.
- As três contas solicitadas foram verificadas com `BELRESTAURANTES_VERIFY_HOMOLOGATION=true`, lendo o arquivo DPAPI sem imprimir senhas. Os testes completos de cookies reais, senha incorreta, bloqueio, matrícula e verificação TOTP, troca obrigatória, logout e restrições de papel usam três contas temporárias persistidas, removidas ao final. MFA não foi cadastrado em nome do operador nas contas definitivas.
- **175 casos responsivos aprovados**, em 360, 390, 768, 1024 e 1440 pixels. Incluem páginas públicas, HTML administrativo capturado em host de teste autorizado e fixtures controladas de catálogo, restaurante, formulário de horários e planejador. Sem overflow horizontal, erros JavaScript ou violações detectadas no conjunto de regras axe WCAG A/AA usado. A inspeção não constitui certificação WCAG completa.
- **40 casos adicionais em inglês/espanhol aprovados**, cobrindo homepage, municípios, guia e busca nas cinco larguras. Total das duas matrizes: **215 casos visuais**.
- **Cinco interações aprovadas no navegador**: geolocalização somente após clique/permissão, plano sem origem, remoção, compartilhamento sem origem e mapa após clique. O teste encontrou e corrigiu o reconhecimento de horários codificados nos planos salvos.
- Pacotes Web e Worker gerados em `artifacts/sprint17/publish/`. Processo local não equivale à instalação do Windows Service nem à publicação no IIS de produção.

O catálogo operacional permanece com **5.747 cadastros pendentes e zero publicados**, sem novas publicações em massa fora do fluxo de confirmação. As fixtures publicadas nos testes foram removidas. Isso mantém explícita a tarefa de curadoria: entrar como moderador, filtrar os elegíveis e confirmar os lotes.

Worker observado com heartbeat ativo e Discovery/Crawler habilitados. Ciclos registrados com início em 11:11:17, 11:12:17 e 11:13:17 UTC demonstram o intervalo de um minuto; tarefas posteriores respeitam a nova frequência por alvo. O primeiro ciclo atualizou dez cadastros reais usando o snapshot de Belém, com zero falhas e sem nova chamada externa nesse ciclo.

Evidências locais (pasta ignorada pelo Git):

- `artifacts/sprint17/build.log`, `unit.trx`, `integration.trx` e `verification-summary.json`.
- `artifacts/sprint17/operations-evidence.json`, `discovery-evidence.json`, `worker.log` e `worker-final.log`.
- `artifacts/sprint17/visual/report.json`, `snapshots/`, `visit-interactions.json` e `visit-desktop-firstfold.png`.
- `artifacts/sprint17/locales/report.json`: 40 casos adicionais, zero falhas.

Os scripts de verificação e os testes estão versionados. Dados de homologação não são apresentados como estabelecimentos reais nem incluídos na contagem do catálogo.

## Publicação segura no GitHub

O primeiro envio da branch local foi bloqueado pela proteção de segredos do GitHub: o commit antigo `7696f68` contém credenciais GCP e Groq, já ausentes dos arquivos atuais. A proteção não foi desativada nem contornada.

O histórico local foi preservado. A branch de revisão `sprint17/cuia-mobilidade-revisao` usa uma fotografia saneada da base local com ancestral remoto limpo, seguida dos commits por módulo. O plano de recuperação já presente no remoto também foi preservado. A comparação das árvores Git verifica que a publicação contém os mesmos arquivos validados neste ambiente. Não há force push nem alteração da branch principal.

Foram verificados os 396 arquivos então rastreados, sem os padrões de credenciais GCP, Groq, OAuth ou GitHub nos arquivos atuais. Isso não substitui a revogação das credenciais antigas pelo titular.


## Compatibilidade de instalação nova no MySQL 8

O CI com banco vazio identificou sintaxe `ADD COLUMN IF NOT EXISTS` nas migrações históricas 014 e 015, aceita no MariaDB e incompatível com MySQL 8. O migrador agora executa essas adições condicionalmente por coluna, consultando `information_schema` sob o mesmo bloqueio de migrações. O adaptador é restrito a esses dois scripts e preserva seus conteúdos e checksums; não libera automaticamente tentativas interrompidas. Três testes adicionais verificam as sete adições, a preservação do ENUM com vírgulas e a restrição do adaptador aos scripts legados.

A validação remota usa MySQL 8.4 em instância efêmera. Os resultados do CI são consultáveis em [GitHub Actions da branch de revisão](https://github.com/devmnsoft/belemrestaurantes/actions?query=branch%3Asprint17%2Fcuia-mobilidade-revisao). Os 87 testes de integração locais foram executados no banco MariaDB existente; a suíte completa de concorrência é reservada ao banco isolado do CI.
