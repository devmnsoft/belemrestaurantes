# Sprint 48 — estabilidade, identidade e descoberta com fontes

Data: 20/10/2026. Execução local em `C:\MNSOFT\belemrestaurantes`, preservando as alterações anteriores das Sprints 46/47. Sem commit, push, deploy, contratação ou ativação de serviço pago. Evidências em `artifacts/sprint48`, ignorado pelo Git.

## Situação verificável

| Item | Situação e limite |
| --- | --- |
| Ranking, tipos MySQL e moderação | **IMPLEMENTADO e TESTADO COM MYSQL REAL**: transação correta, rollback, concorrência, cancelamento, idempotência e exclusão dos aprovados da fila pendente. |
| Administração após MFA | **IMPLEMENTADO e TESTADO** com autenticação real no host de integração; esquemas explícitos e retorno administrativo seguro. Nenhuma credencial administrativa real foi alterada. |
| Cadastro por CPF/e-mail e representação por CNPJ | **IMPLEMENTADO e TESTADO COM MYSQL REAL**, incluindo requisições HTTP, concorrência, confirmação e recuperação. Entrega real de e-mail **AGUARDANDO SMTP**. |
| Cuia além do catálogo | **HOMOLOGADO NO PORTAL LOCAL** com referência editorial real e revisada do Na Maré. Resposta factual do índice, sem geração por LLM; não representa busca aberta em tempo real. |
| Gemini | Adaptador e tratamento de falhas **IMPLEMENTADOS e TESTADOS COM SIMULAÇÃO**. A chamada real desta Sprint retornou **503**, sem geração. Não declarar o fluxo generativo homologado. |
| Pesquisa aberta | Integração existente e testes preservados; **AGUARDANDO CONTA/CHAVE DE BUSCA**, sem criar conta ou habilitar cobrança. |
| Robô | **HOMOLOGADO COM SERVIÇO REAL E PROCESSO LOCAL**: snapshot OSM atualizado, importação, ciclo contínuo, parada e reinício comprovados. Não foi instalado serviço de inicialização do Windows. |
| Fotos | Três fotos reais adicionais, WebP responsivo, autoria e licenças documentadas; seis fotografias na coleção final. |
| Vídeos e avaliações | Extração de links de vídeos em dados estruturados, revisão e apresentação implementadas. Avaliações do Google encaminham ao serviço; não foram copiadas. Nenhum vídeo específico novo foi homologado. |
| Mobilidade | Recursos e testes existentes preservados. Não há nova homologação de feeds de ônibus/BRT/barcos nem geometria multimodal real. |
| Contraste e mobile | Cenários automatizados e capturas descritos abaixo, incluindo Chromium e WebKit. Não constituem certificação universal de dispositivos ou WCAG integral. |

## Diagnóstico e correções

### Banco, cancelamento e moderação

- `MySqlCuiaService` passava a transação como objeto de parâmetros no comando de exclusão. Agora todos os comandos usam explicitamente a transação ativa. Um lock nomeado serializa recalculações; ranking vazio também limpa entradas antigas. Falha de inserção preserva o ranking anterior. Liberação do lock tem prazo próprio limitado.
- O DTO posicional `MemberAdminView` não correspondia aos nomes e tipos reais do resultado, especialmente `UInt32 failed_attempts` e `DateTime`. Uma linha intermediária recebe os tipos MySQL e faz o mapeamento explícito, incluindo nulos e UTC.
- Cancelamento do navegador, timeout interno e falha no banco são tratados separadamente. Na autenticação, indisponibilidade retorna 503 sem autenticar, apagar cookie válido ou mostrar stack trace. Isso não elimina cancelamentos legítimos nem aumenta timeouts indiscriminadamente.
- A listagem antiga de pendentes filtrava somente `status='pending'`, embora aprovados ainda aguardassem publicação. Agora exige também `moderation_stage='pending'`; transições individuais mantêm os estados consistentes. Contagem e paginação usam o mesmo critério.
- Confirmação em lote continua transacional, com recuperação do resultado por GET apenas pelo ator original, resposta sem cache e proteção contra reenvio. O teste bloqueia uma linha real, cancela, comprova ausência de decisão parcial, repete e verifica auditoria única. Não foi criada fila assíncrona de lotes nem homologado lote de mil itens.

Referência técnica: [uso de transações no MySqlConnector](https://mysqlconnector.net/troubleshooting/transaction-usage/).

### Identidade, MFA, cadastro e senhas

- Policies administrativas agora selecionam explicitamente o esquema administrativo, separado do esquema de membros. MFA não transforma cookie de membro em administrativo. Retorno após MFA aceita apenas caminhos locais administrativos canônicos, recusando login/MFA, rotas de membros e caminhos manipulados. O layout oferece o destino administrativo correto.
- CPF e e-mail identificam a conta pessoal; CNPJ identifica a organização representada, permitindo solicitações de vínculo de pessoas distintas. Documento ou e-mail coincidente não concede função administrativa nem verificação empresarial.
- CPF obrigatório no cadastro HTTP; validação de dígitos, normalização e índices únicos no banco. CNPJ numérico e alfanumérico usam a regra de dígito verificador da Receita. Letras Unicode e documentos inválidos são recusados. [Manual oficial do CNPJ](https://www.gov.br/receitafederal/pt-br/centrais-de-conteudo/publicacoes/documentos-tecnicos/cnpj/manual-dv-cnpj.pdf).
- Não são armazenados documentos completos. O índice de busca usa HMAC com chave persistente protegida por Data Protection; apenas os quatro caracteres finais são guardados para apresentação. A verificação matemática não comprova titularidade.
- Organizações e solicitações de vínculo são separadas; novo pedido fica pendente e não sobrescreve a razão social declarada anteriormente. Registros legados sem CPF continuam compatíveis; não houve associação ou fusão automática de pessoas. Zero e-mails normalizados duplicados na auditoria local.
- A configuração local não possuía `MailEncryption:KeyRingPath`, necessária ao fluxo de e-mail. Foi definido um diretório privado compartilhado por Web e Worker, fora do repositório. Preservar esse diretório, inclusive `document-lookup.v1.bin`, em backup e entre instâncias; perder a chave impede comparar os documentos existentes.
- Reenvio tem intervalo mínimo; mensagens públicas são neutras e não revelam existência de conta. Recuperação mantém validade, uso único e revogação de sessão; não altera nem contorna MFA administrativo. SMTP local está desabilitado: a interface informa indisponibilidade e não afirma envio.
- Botões mostrar/ocultar senha nos dois layouts, com controles acessíveis, senha inicialmente oculta e manutenção do valor. Testados por clique/toque emulado; não foi armazenada senha no navegador para esse recurso.

Referência: [seleção de identidade por esquema no ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/limitingidentitybyscheme?view=aspnetcore-10.0).

### Cuia e referência fora do catálogo

- Índice independente `place_reference_index`: lugar, município, URL, editor, natureza da fonte, observação, validade, fatos, horários e links de vídeo. Não exige ficha publicada e não publica restaurantes.
- Extração de `MenuItem`, horários e vídeos em Schema.org alimenta referências pendentes. Página estruturada não é automaticamente considerada oficial. Mudança de fatos exige nova revisão; observação idêntica não reabre decisão aprovada ou rejeitada. Expiradas deixam de fundamentar respostas.
- `/admin/referencias` permite conferir a fonte e aprovar/rejeitar com autorização, antiforgery e auditoria. URLs públicas HTTPS, limites de conteúdo e vídeos de domínios permitidos; sem baixar vídeos ou apresentar texto externo como instrução confiável.
- Perguntas sobre pratos podem usar fatos revisados sem LLM. Resposta distingue reportagem de cardápio oficial atual e apresenta fontes e opção externa guardável. Homônimos levam a pedido de esclarecimento; ausência de configuração de busca não vira alegação de pesquisa sem resultados.
- Pesquisa aberta continua condicionada à configuração e à escolha do usuário. Foram preservados os cartões externos, guardar passeio, indicação para moderação e histórico curto.

**Caso real Na Maré:** pesquisa manual encontrou a [referência editorial VEJA Comer & Beber](https://veja.abril.com.br/comer-e-beber/belem/estabelecimento/na-mare/), Rua São Boaventura, 112, Belém. Foi registrado apenas o fato da menção ao prato “Égua mano” com peixe, tucupi e jambu, com aviso de disponibilidade atual não confirmada. Referência local #14, revisada explicitamente, validade de sete dias. Não foi copiado artigo nem criado restaurante publicado. O navegador comprovou HTTP 200, prato citado, ressalva e links de fonte na pergunta sobre os pratos do Na Maré. Isso não comprova descoberta automática de qualquer cardápio na internet.

### IA gratuita e falhas externas

- Gemini preferindo HTTP/2 com negociação de versão, conexão limitada e resposta limitada em bytes. Parser separa partes de raciocínio da resposta, rejeita término incompleto e contabiliza tokens de raciocínio quando informados. Erros não expõem corpo remoto/chave.
- Timeout, cancelamento do usuário, 429, credencial recusada, modelo inexistente e indisponibilidade têm estados distintos. Não há repetição ilimitada nem resposta apresentada como gerada quando o provedor falha.
- Worker também passa pela política de provedores gratuitos confirmados e pelas reservas persistentes. Cota compartilhada com Cuia: 20 tentativas/dia, 200/mês; finalidade Worker: 2/dia, 20/mês. Sessão Cuia: 24.000 unidades conservadoras; saída: 800 tokens. Falhas consomem reserva. Valores locais em user-secrets, sem chaves versionadas. Pausa administrativa continua prevalecendo.
- Homologação .NET desta Sprint: `gemini-3.1-flash-lite`, chamada curta fundamentada na referência revisada, **503 em cerca de 1,7 s**, `generated=false`, nenhuma saída gerada. Reserva Worker passou a 1/dia e 1/mês; conta compartilhada registrava 8/dia e 8/mês no levantamento (inclui uso/reservas anteriores). Tokenização não foi informada pela resposta de erro; não foi inventado total. Orçamento monetário local permaneceu zero.
- Teste exploratório único de `gemini-2.5-flash` com grounding: 404, sem geração nem tokens informados. Não foi incorporado como solução. Grounding pago de modelos 3.x não foi habilitado. [Preços e condições do Gemini](https://ai.google.dev/gemini-api/docs/pricing).
- Groq não recebeu chave Gemini. Nenhuma chave foi repetida em relatório ou logs de teste preparados. Não houve nova tentativa repetitiva para mascarar a indisponibilidade externa.

### Robô, profundidade e execução contínua

- Causa operacional constatada: ausência de Worker vivo, heartbeats antigos ainda marcados `running`, alvos de Belém com próxima execução semanal. O painel considera heartbeat vencido após 90 segundos.
- Página de snapshot OSM agora tem ordenação estável e cursor durável por alvo, com offset, total, versão e indicação de continuação. Cursor e conclusão do job são gravados juntos. Snapshot novo reinicia a paginação; continuação volta à elegibilidade após cinco minutos, respeitando a cadência efetiva do Worker.
- Descoberta não reenfileira revisão como “novo lugar” a cada observação de um existente. Novos candidatos seguem para revisão, conflitos têm fluxo próprio e dados dos parceiros são preservados.
- Atualização real do snapshot de Belém em **20/10/2026 15:54:33 UTC**, após duas falhas com prazo limitado e sucesso no terceiro endpoint. Licença ODbL e cache preservados. A amostra regional contém **483 resultados**; não representa todos os restaurantes da cidade.
- Importação manual limitada: **80 encontrados, 0 novos, 80 atualizados, 0 falhas**. Não confundir atualização com expansão de catálogo publicado.
- Nove alvos de Belém já habilitados foram ajustados para OSM, atualização diária e lotes de 100, preservando prioridades, pausas e alvos desabilitados. Foram enfileirados para execução normal.
- Supervisor local iniciado às 16:10:10 UTC: primeiro ciclo de Belém com **100 encontrados, 0 novos, 100 atualizados, 0 falhas**, cursor regional persistido em 100 de 483. Parada ordenada confirmada às 16:11:02 UTC; retomada às 16:11:23 UTC com novo processo e heartbeat recente. O cursor anterior permaneceu intacto. O próximo alvo do plano estadual já habilitado foi Muaná: **5 encontrados e 5 novos candidatos**, sem falhas. Não foram publicados automaticamente.
- Auditoria antes da retomada: 25.062 candidatos pendentes e 179 publicados no banco local; Belém tinha 4.864 registros, 3.481 com endereço e 481 com coordenadas. Isso mostra também um gargalo de qualidade e revisão, não apenas falta de coleta. Totais incluem o histórico local; não atribuir todos a esta Sprint.
- Worker local: fila a cada minuto, coleta externa a cada dez minutos, um alvo por ciclo e até duas tarefas de crawler. Cotas não são contornadas; fontes pausadas continuam pausadas. Supervisor reinicia processo que termina inesperadamente com atraso progressivo. Esse mecanismo está implementado; o ensaio real desta entrega foi parada ordenada e novo início, sem provocar crash durante gravação.
- O processo ficou rodando localmente ao concluir a entrega. Só funciona enquanto a máquina e o supervisor estiverem ativos; não houve instalação de serviço/tarefa, inicialização automática do Windows nem deploy. Operação detalhada em [LOCAL-WORKER.md](LOCAL-WORKER.md).

### Fotos, vídeos, avaliações e rotas

Três acréscimos reais da Wikimedia Commons, com duas resoluções WebP, dimensões, carregamento tardio, autor, link e licença por imagem:

| Foto | Autor | Licença | Fonte |
| --- | --- | --- | --- |
| Maniçoba | Ana_Cotta | CC BY 2.0 | [Manicoba.jpg](https://commons.wikimedia.org/wiki/File:Manicoba.jpg) |
| Pato no tucupi | marcusrg - Hello, nice to meet you! | CC BY 2.0 | [Pato no Tucupi](https://commons.wikimedia.org/wiki/File:Pato_no_Tucupi_(2113297642).jpg) |
| Feira do Açaí, Belém | Túllio F | CC BY-SA 4.0 | [Feira do Açaí — Belém 02](https://commons.wikimedia.org/wiki/File:Feira_do_A%C3%A7a%C3%AD_-_Bel%C3%A9m_02.jpg) |

Origem, alterações de formato e tamanhos estão em `wwwroot/images/para-real/credits.json`. Adaptações mantêm a licença aplicável. Imagens foram inspecionadas e são apresentadas como documentação cultural, sem afirmar que retratam restaurantes recomendados.

Vídeos são links externos revisáveis; reviews do Google não foram extraídos nem republicados indiscriminadamente. Detalhes oferecem busca do perfil e avaliações no Maps, identificada como busca. Atribuição não substitui autorização/licença; [políticas do Places](https://developers.google.com/maps/documentation/places/web-service/policies).

Não se calculam todas as rotas possíveis. Fluxos existentes distinguem orientação local, geometria obtida de fonte e abertura externa. Horários do restaurante não viram horários de transporte. Cobertura real de linhas, tarifas, ônibus/BRT, balsas/barcos/intermunicipais e voos continua dependente de fontes e provedores configurados; não foi fabricada.

## Migrações e configuração privada

- **036**: documentos pseudonimizados, organizações e pedidos de vínculo; FK compatível com o identificador legado de membro. Colunas pessoais opcionais para compatibilidade histórica; novos cadastros HTTP exigem CPF.
- **037**: progresso durável de snapshots nos alvos de descoberta.
- **038**: referências externas independentes do catálogo publicado.

Aplicadas e verificadas no MySQL local. Migrações 034/035 e alterações prévias foram preservadas. Revisar backup e configuração de chaves antes de aplicar em outro ambiente; a entrega não migrou produção.

Os segredos continuam fora do repositório nos cofres de desenvolvimento Web/Worker. `MailEncryption:KeyRingPath` local aponta para `%LOCALAPPDATA%/MNSOFT/BelemRestaurantes/mail-keys`. A chave Gemini foi compartilhada somente entre esses cofres locais autorizados; não foi exposta em configuração versionada. Não criar nova chave de documento por instância: compartilhar persistentemente o mesmo diretório e a proteção compatível.

## Verificação e evidências

- Solução Release: **zero erros e zero avisos** (`build-final.log`); recompilação do projeto de testes após acrescentar caso de referências (`build-tests-final.log`).
- Unitários: **347 aprovados**, zero falhas (`unit-final.log`).
- Integração final: **192 aprovados, um ignorado**, zero falhas (`integration-final.log`). O ignorado é o teste preexistente de credenciais administrativas persistentes; não se alteraram senhas reais para habilitá-lo.
- MySQL real: rollback com falha intermediária, recálculos concorrentes, materialização, bloqueio/cancelamento em lote, idempotência, documento/e-mail concorrentes, compartilhamento de organização sem privilégio, cursor atômico, revisão/expiração de referências e retorno HTTP 503 sem autenticação indevida.
- UI Sprint 48: **42 cenários** nas larguras 320/390/768/1440, incluindo erros de CPF, recuperação, mostrar senha, fontes do Na Maré e texto a 200% (`ui/report.json`).
- Administração e áreas privadas: **14 combinações** de sete telas em 320/1440 e **quatro cenários adicionais** a 200% em 320, sem overflow, erros JS ou problemas apontados nas regras verificadas (`private-ui/report.json`, `private-text200/report.json`). HTML autenticado vem de host de teste, sem bypass acrescentado à aplicação. Axe executado em 320; demais larguras conferem layout e erros.
- Plataformas: **24 combinações** Pixel 7/Chromium e iPhone 13/WebKit, retrato/paisagem, seis rotas, status 200, sem overflow/erros JS e controles de senha funcionais (`platforms/report.json`). Capturas são da janela visível para respeitar o limite de pixels do WebKit. Não é teste de aparelhos físicos ou Safari nativo.
- Regressão de descobertas e passeio: **sete cenários**, incluindo persistência, ordem, fonte externa simulada, histórico, falhas de rede/mapa e texto ampliado (`journeys/report.json`, `journeys.log`). Provedor da conversa dessa regressão é simulado.
- Verificação complementar: mostrar/ocultar senha por Espaço e Enter, valor preservado; seis imagens decodificadas pelo navegador e três novos destinos de pratos com HTTP 200 (`extra-ui.json`). Scripts PowerShell e sete arquivos JavaScript passaram pela verificação de sintaxe; `git diff --check` sem erros e varredura dos padrões de credenciais sem ocorrências nos diretórios de código/documentação verificados.
- Serviço real: `open-data-live.log`, `ai-live.log`, `grounding-probe.json`, `worker-after.log`, `worker-after-restart.json`; logs de processo em `artifacts/worker-local`. Capturas principais em `ui`, `platforms` e `private-ui`.

Testes automatizados de IA usam provedores simulados e não fazem chamadas reais repetitivas. A última repetição dos testes de fila com o Worker ligado teve interferência entre consumidores no mesmo banco; o Worker foi parado, a suíte completa passou isoladamente e a execução local foi retomada ao final. Para reproduzir essa suíte no banco local, pare o Worker ou use um banco de testes separado. Os dados de teste que exigem MySQL são limpos pelos testes; o inventário legado pode conter registros de sessões anteriores. O teste de registro de novas contas não prova entrega de e-mail enquanto SMTP estiver desligado.

## Aguardando configuração ou fonte

1. **SMTP**: envio real de confirmação, reenvio e recuperação; homologar caixa de destino e links no ambiente final.
2. **Busca aberta**: conta/chave de provedor suportado, guardada no cofre. Não foi fornecida resposta à consulta sobre esse recurso; coleta OSM, fontes revisadas e links continuam disponíveis.
3. **Gemini**: resposta generativa de ponta a ponta sem 503/timeout; o caso factual Na Maré funciona independentemente dessa disponibilidade. Evitar consumir cota com tentativas repetidas.
4. **Mobilidade**: credenciais próprias do Maps/Routes/Places quando adotados e feeds locais vigentes com autorização de uso. Identificador/segredo OAuth não substitui chave de Maps.
5. **Dados editoriais**: fontes atuais para cardápios e horários, tratamento dos candidatos sem endereço/coordenada e revisão do grande estoque pendente. Não há promessa de catálogo completo de Belém.
6. **Execução permanente**: instalação supervisionada no ambiente autorizado em tarefa posterior. Esta entrega mantém apenas execução local e documentação.
