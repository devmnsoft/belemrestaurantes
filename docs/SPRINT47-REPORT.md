# Sprint 47 — Cuia, descobertas e leitura no celular

Data: 20/10/2026. Trabalho local em `C:\MNSOFT\belemrestaurantes`, preservando alterações anteriores da Sprint 46. Sem commit, push ou deploy. Evidências de testes e capturas estão em `artifacts/sprint47`, ignorado pelo Git.

## Situação da entrega

| Recurso | Implementação e validação |
| --- | --- |
| Cuia contextual, histórico curto, contingência honesta | Implementado; testes unitários e HTTP aprovados com provedores simulados. |
| Gemini gratuito | Chave configurada apenas no user-secrets local após confirmação do usuário. Geração real curta e JSON comprovadas diretamente na API. **Fluxo completo do portal ainda não homologado com sucesso**: tentativas retornaram 503 ou excederam o prazo; a aplicação apresentou orientação local identificada. |
| Sugestões fora do catálogo com fontes | Contrato, validação, cartões, guardar e indicação para moderação implementados e testados com fontes simuladas. Pesquisa real aguarda configuração de Tavily; a IA não apresenta memória do modelo como pesquisa atual. |
| Groq | Adaptador e contingência preservados; não homologado com serviço real. A chave identificada como Gemini não foi enviada ao Groq. |
| Descobrir, guardar e passeio editável | Implementados e validados no navegador, com dados publicados reais e uma opção externa de teste identificada. |
| Contraste e responsividade | Verificados nos cenários descritos abaixo; não constituem certificação integral de acessibilidade. |
| Mapa | Carregamento, recuperação, cancelamento e atualização de marcadores melhorados; medição local antes/depois concluída. |
| Ônibus/BRT/barco/balsa/avião | Serviços existentes preservados. Sem nova fonte operacional habilitada, não há nova cobertura ou traçado de transporte homologado. |

## Causas encontradas e correções

A resposta da Cuia dependia excessivamente de restaurantes conferidos; uma resposta cultural podia ser descartada quando o catálogo não tinha correspondência. O serviço agora aceita resposta cultural contextual e separa catálogo conferido, fichas e sugestões externas. A orientação editorial local tem identificação explícita e não se apresenta como geração de IA.

O contexto das perguntas seguintes não era suficiente para manter a conversa. Página e widget agora enviam contexto curto, preservam a preferência de pesquisa durante a sessão e permitem reiniciar a conversa. IDs de lugares são conferidos novamente no servidor. Perguntas claramente alheias ao portal recebem redirecionamento sem chamada ao modelo; solicitações ambíguas continuam sujeitas às instruções contextuais. Isso reduz respostas fora do escopo, mas não equivale a garantia absoluta contra toda formulação adversarial.

Sugestões externas usam JSON estruturado. O servidor exige nome apoiado em título/trecho de fonte, índices válidos e evidência extraída; cidade e bairro sem apoio são retirados. Lugares já publicados não reaparecem como novos. Links de navegação são montados pela aplicação. Resultado externo nunca publica restaurante nem concede status de conferido. O visitante pode indicar a opção, com fonte preenchida como nota para revisão. A migração 035 e bloqueio por identidade evitam indicações pendentes duplicadas, inclusive concorrentes.

Havia dois manipuladores do botão de orientação do restaurante: uma única ação fazia duas consultas e o código antigo sobrescrevia sucesso/erro. O manipulador duplicado foi removido; restaurante e receita compartilham tratamento acessível de carregamento, falha e nova tentativa. As dicas locais não prometem geração por IA.

Texto ampliado fazia valores da ficha e informações de rendimento da receita ultrapassarem as colunas. Foram ajustados quebra de palavras, largura mínima e disposição no celular. Um parágrafo da receita herdava cor escura em fundo verde; recebeu cor explícita adequada. O painel móvel da Cuia também ficava atrás dos controles de leitura: a ordem de sobreposição foi corrigida.

No WebKit, a política `upgrade-insecure-requests` tentava carregar recursos HTTPS em um servidor local que só atendia HTTP. A exceção foi limitada ao ambiente Development; produção conserva a diretiva. Os ícones Bootstrap 1.11.3 passaram a ser servidos localmente, eliminando um CSS de CDN bloqueado pela própria política do site. Licença MIT preservada em `wwwroot/vendor/bootstrap-icons/LICENSE`.

## Uso gratuito e limites por sessão

O usuário confirmou por último que o projeto Gemini está no nível gratuito e autorizou o uso. Isso substituiu a informação anterior de cobrança ativada. A chave foi autenticada e salva somente em user-secrets fora do repositório, sem copiar valores para código, appsettings ou relatório. A confirmação do plano vem do usuário; o endpoint de modelos não certifica o estado de cobrança da conta.

O modelo anterior `gemini-2.5-flash-lite` respondeu 404 no teste de geração. `gemini-3.1-flash-lite` respondeu a chamadas reais e passou a ser o padrão. A [tabela oficial de preços](https://ai.google.dev/gemini-api/docs/pricing) lista entrada e saída gratuitas para esse modelo no Free Tier; não foram habilitados grounding pago, Maps grounding ou fallback pago.

Configuração efetiva **local**, sem credenciais:

```json
{
  "Ai:Gemini:Model": "gemini-3.1-flash-lite",
  "Cuia:FreeTier:ConfirmedFreeProviders:0": "Gemini",
  "Cuia:FreeTier:DailyRequestLimit": 20,
  "Cuia:FreeTier:MonthlyRequestLimit": 200,
  "Cuia:FreeTier:SessionTokenLimit": 24000,
  "Cuia:FreeTier:MaxOutputTokens": 800
}
```

- Até duas perguntas anteriores, com 300 caracteres cada; até três trechos de fonte de 700 caracteres. O servidor limita a pergunta atual a 500 caracteres e valida o corpo recebido.
- Reserva atômica antes da tentativa: bytes UTF-8 do pedido serializado, mais teto de saída e margem de 256 unidades. É uma estimativa conservadora, **não uma contagem exata por tokenizer**. Falhas também consomem reserva.
- Cookie de uso protegido por Data Protection, HttpOnly, SameSite=Lax, válido por 24 horas e Secure em HTTPS. A cota é independente do botão de reiniciar conversa.
- O saldo da sessão usa períodos fixos durante sua vida: virar o dia ou o mês não libera outro saldo para o mesmo identificador. As cotas globais usam dia/mês UTC e persistem no MySQL.
- Limpar cookies ou trocar navegador pode criar outra sessão. O controle por sessão não é uma barreira absoluta contra abuso; a cota global compartilhada e os limites do provedor continuam valendo.
- Contadores não armazenam texto da conversa, IP ou segredo. Sessões inativas há mais de dois dias são removidas, em lotes de até 100 linhas por reserva. Política de privacidade atualizada.
- Cota ausente, esgotada ou indisponível impede a tentativa. Pausa administrativa permanece efetiva. O orçamento monetário local foi verificado em zero e a pausa do Gemini já estava desativada; não houve aumento de orçamento pago.

A chamada JSON real levou aproximadamente 19,8 segundos. Por isso o prazo da cadeia foi ajustado para 37 segundos, com até 8 segundos para Groq e 28 para Gemini, uma tentativa por provedor. Página e widget cancelam em 50 segundos; a pesquisa tem seu próprio prazo. Não há repetição automática ilimitada. Os limites da aplicação não substituem cotas ou configurações de cobrança da conta Google.

Evidências reais: resposta curta com 52 tokens; JSON cultural válido com 174 tokens. `gemini-live-json.json` registra o resultado sem credencial. `gemini-app-final.json` registra a tentativa completa que recebeu orientação local após indisponibilidade. Um diagnóstico mínimo por HttpClient também excedeu 28 segundos. Assim, autenticação e geração direta foram comprovadas, mas estabilidade pelo servidor .NET permanece pendente. Os testes automatizados usam provedores simulados ou chaves desabilitadas no processo para não consumir a conta real.

## Experiências de descoberta

`/descobrir` oferece escolhas por momento, município e sabor, conectando lugares publicados, pratos e receitas. “Família” e “a dois” são pontos de partida editoriais, não confirmação de estrutura, disponibilidade ou acessibilidade de cada estabelecimento.

“Guardar para depois” está nos cartões, fichas e respostas da Cuia. `/meu-passeio` funciona sem login, mantém até vinte escolhas neste navegador, permite ordenar, remover e apagar, recarrega somente fichas ainda publicadas e permite importar favoritos da conta autenticada sem alterá-los. Opções externas mantêm origem e identificação. Planos de chegada existentes também aparecem, sem gravar a origem do visitante. Falha ou bloqueio de armazenamento é informado; não há confirmação falsa de salvamento.

Cada parada permite continuar para a ficha ou fonte e para o planejamento de chegada. Não foram inventados tempos entre paradas ou horários. Links relacionados conectam prato, receita, busca, descoberta e Cuia.

## Fotografias e licenças

Três fotografias reais foram selecionadas, verificadas visualmente e convertidas para WebP em duas larguras. Há dimensões declaradas, srcset, carregamento tardio, legenda, autor, origem e licença visíveis. Elas ilustram cultura e paisagem, sem fingir ser fotos de restaurantes específicos.

| Foto | Autor | Fonte e licença |
| --- | --- | --- |
| Ver-o-Peso, Belém | Cayambe | [Original](https://commons.wikimedia.org/wiki/File:Bel%C3%A9m_Ver-o-Peso_01.jpg), CC BY-SA 3.0 |
| Tacacá | MI GENERAL ZAPATA | [Original](https://commons.wikimedia.org/wiki/File:Tacac%C3%A1.jpg), CC BY-SA 3.0 |
| Alter do Chão, Santarém | LuciaBarreirosdaSilva | [Original](https://commons.wikimedia.org/wiki/File:Praia_de_Alter_do_Ch%C3%A3o_Santar%C3%A9m_Para_Amazonia_Brasil.jpg), CC BY-SA 3.0 |

Metadados, tamanhos e alterações estão em `src/BelemRestaurantes.Web/wwwroot/images/para-real/credits.json`. As versões menores têm aproximadamente 35, 62 e 37 KB. As adaptações das imagens permanecem sob [CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/).

## Mapa: confiabilidade e medição

O carregamento passa a aguardar CSS e JavaScript do Leaflet, com prazo e recuperação. A lista permanece acessível na falha; mostrar a lista cancela a consulta e impede que uma resposta antiga reabra o mapa. Há prazo na consulta, cancelamento e descarte de respostas fora de ordem. O agrupamento usa células da tela e os marcadores sem alteração são reaproveitados; a lista é montada em um fragmento de documento.

Comparação local com o mesmo servidor, catálogo e viewport 390×844: cinco aberturas em contextos novos para cada JavaScript e quinze consultas de API, com conexão aquecida. A versão anterior foi obtida do HEAD e interceptada somente no navegador; não houve checkout ou alteração do trabalho existente. As duas rodadas finais foram sequenciais, sem auditoria de páginas simultânea.

| Medida | JavaScript anterior | Atual |
| --- | ---: | ---: |
| Mediana clique → mapa útil | 168 ms | 141 ms |
| API p50 | 15,97 ms | 16,40 ms |
| API p95 | 24,75 ms | 24,35 ms |

A abertura local foi cerca de 16% menor nessa amostra pequena. Não é promessa para produção, nem evidência de aceleração da consulta SQL. O mapa foi medido sem tiles de rua configurados e sem roteamento externo. As primeiras medições, feitas sob cargas diferentes, foram descartadas da comparação. Evidências finais: `map-baseline-fair.json` e `map-final-fair.json`; script `scripts/measure-map.cjs`.

## Testes e evidências

| Verificação | Resultado |
| --- | --- |
| Build Release da solução | Zero avisos e erros |
| Testes unitários | 330 aprovados |
| Integração completa | 181 aprovados, 1 ignorado, zero falhas |
| HTTP/segurança após ajustes de CSP | 39 aprovados |
| Quotas e jornadas após ajuste final da duração da sessão | 10 aprovados |
| Páginas públicas | 98 combinações de rota/largura, zero falhas detectadas |
| Conta, parceiro e administração | 28 combinações sobre HTML autenticado exportado pelos testes, zero falhas detectadas |
| Texto a 200% | 20 combinações, zero overflow e falhas detectadas |
| Estados críticos de componentes | 8 cenários, zero falhas detectadas |
| Jornadas guardar/passeio/Cuia/erro/mapa | 7 estados inspecionados, zero falhas e erros JavaScript |
| Plataformas móveis emuladas | 16 combinações Chromium/Pixel 7 e WebKit/iPhone 13, retrato e paisagem; zero overflow/erros, alvos medidos de pelo menos 44 px |

O teste ignorado é a verificação existente de contas administrativas persistentes; não foi criada exceção nova para ocultar falha. A suíte completa passou antes do último refinamento de períodos fixos da sessão; os dez testes pertinentes foram repetidos e passaram após ele.

As auditorias usam axe nos estados críticos e na largura 320 da matriz; as demais larguras verificam layout, tamanho mínimo e erros. A matriz pública percorreu 320, 360, 390, 430, 768, 1024 e 1440 px. WebKit em Windows com perfil de iPhone não certifica Safari físico, Android real ou todos os teclados móveis.

Jornadas verificadas: guardar dois lugares reais, ordenar e persistir, adicionar opção externa da fixture, remover, apagar, armazenamento negado, histórico e preferência de pesquisa, reiniciar conversa, orientação local com falha e nova tentativa, fechamento com Escape e retorno de foco, falha de CSS do mapa e recuperação. A sugestão externa usa fixture HTTP/LLM e não comprova pesquisa real. Quotas usam MySQL real e concorrência; o limite de sessão bloqueia chamadas ao provedor antes de gerar.

Capturas e JSON ficam em `artifacts/sprint47/{public,private,text200,states,journeys,platforms}`. Capturas adicionais de leitura real em viewport: `discover-viewport.png` e `photos-viewport.png`. Logs de build e testes ficam na mesma pasta. Scripts: `verify-sprint47.cjs`, `verify-mobile-platforms.cjs`, `verify-sprint44.cjs` e `verify-sprint45-states.cjs`. As capturas privadas contêm estado de teste e não devem ser publicadas.

## Banco e pendências

A migração 034 mantém cotas persistentes. A 035 acrescenta chave de deduplicação às sugestões. A primeira proposta da 035 usava expressão gerada incompatível com o MariaDB local; ela falhou antes de criar colunas. A recuperação foi feita somente após verificar zero colunas parciais, zero aplicação e uma tentativa própria incompleta. A definição foi simplificada para coluna normal com índice e reaplicada pelo MigrationRunner. O bloqueio de migrações e suas salvaguardas foram preservados. `recovery.log` contém a verificação anterior e a aplicação bem-sucedida. Não executar novamente o utilitário temporário de diagnóstico em artifacts.

Pendências externas concretas:

1. Homologar uma resposta gerada de ponta a ponta no servidor .NET quando o Gemini estiver disponível; não tratar os 503 como êxito. A chave e as cotas locais já estão configuradas.
2. Configurar uma conta Tavily gratuita própria, segredo `WebSearch:Tavily:ApiKey`, habilitação e confirmação do plano para validar descobertas externas reais. O cenário sem configuração é informado ao visitante.
3. Configurar e homologar um provedor de rotas, chave adequada e fontes vigentes de transporte local. Credenciais OAuth não equivalem a chave de Maps. Sem geometria válida não se desenha trajeto; sem fonte não se inventam ônibus, BRT, travessias ou horários.
4. Verificar em aparelhos físicos e ambiente HTTPS publicado, com seus dados e permissões, antes de afirmar compatibilidade universal.

Não houve publicação em massa de restaurantes, alteração de credenciais no código, commit, push ou deploy. As alterações estão disponíveis localmente para revisão.
