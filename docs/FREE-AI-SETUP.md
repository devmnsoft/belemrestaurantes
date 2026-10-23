# Cuia: provedores com cota gratuita e pesquisa externa

Consulta de documentação: 20/10/2026. Planos gratuitos têm limites e podem mudar. Na Sprint 47, após confirmação do usuário sobre o plano gratuito, Gemini foi configurado apenas em user-secrets fora do repositório e testado com chamadas reais. Consulte [SPRINT47-REPORT.md](SPRINT47-REPORT.md) para a homologação atual. Groq e Tavily continuam dependendo de configuração própria.

## Escolha

| Serviço | Uso no projeto | Condição |
| --- | --- | --- |
| Groq | Primeira tentativa de resposta da Cuia | Conta Free; limites variam por modelo. Modelo padrão atualizado para `openai/gpt-oss-20b`, listado no plano gratuito consultado. |
| Gemini | Segunda tentativa de resposta | Conta no Free Tier; padrão `gemini-3.1-flash-lite`, com geração gratuita na tabela consultada. |
| Tavily | Pesquisa com títulos, trechos e links | Plano Researcher: 1.000 créditos/mês sem cartão; busca `basic` custa um crédito. Implementação limita esta instalação a no máximo 900 tentativas/mês. |
| Catálogo local | Resposta de contingência | Não depende de credencial externa. Não simula pesquisa nem geração por modelo. |
| OpenRouter | Alternativa pesquisada, não integrada | Plano gratuito consultado: 50 requisições/dia; não foi acrescentado um quarto serviço com novas credenciais. |
| Ollama local | Alternativa pesquisada, não instalada | Inferência local dispensa chave da nuvem; exige modelo, licença compatível e capacidade de hardware. Não equivale a infraestrutura gratuita. |

Fontes oficiais: [Groq: limites](https://console.groq.com/docs/rate-limits), [Groq: modelos](https://console.groq.com/docs/models), [Gemini: preços](https://ai.google.dev/gemini-api/docs/pricing), [Tavily: créditos](https://docs.tavily.com/documentation/api-credits), [Tavily: contrato de busca](https://docs.tavily.com/documentation/api-reference/endpoint/search), [OpenRouter: planos](https://openrouter.ai/pricing/), [Ollama: API](https://docs.ollama.com/api/introduction).

Não existe garantia de custo zero baseada apenas no formato de uma chave. Use contas gratuitas, sem upgrade automático ou cobrança excedente. No Gemini gratuito, a tabela informa uso dos dados para melhoria dos produtos: não envie informações pessoais, coordenadas precisas, segredos ou dados de parceiros. A Cuia envia somente o contexto mínimo do catálogo e da pergunta; pesquisa externa exige a opção do visitante.

## Implementado

- Cadeia exclusiva da Cuia: Groq → Gemini → resposta local. DeepSeek não participa desta cadeia; outros módulos mantêm seus próprios controles e orçamentos.
- Sem tentativa de nuvem até o operador configurar explicitamente quais contas gratuitas foram verificadas. Credencial presente e provedor habilitado para a Cuia aparecem separadamente no endpoint de status.
- Uma tentativa por provedor, até oito segundos para Groq e 28 segundos para Gemini, com prazo de 37 segundos para a cadeia. Cancelar a requisição interrompe as tentativas. Pesquisa tem prazo adicional de seis segundos para a chamada HTTP.
- Resposta cultural não é mais descartada por falta de restaurantes conferidos. O motor exibido corresponde ao provedor utilizado.
- Classificações continuam exigindo JSON; concierge solicita JSON estruturado. Modelos padrão de Web e Worker atualizados sem alterar credenciais ou ativar serviços.
- Pesquisa opcional via Tavily, sem busca avançada, extração, crawling ou resposta gerada pelo próprio buscador. Links externos são apresentados separadamente; nenhum resultado publica ou promove uma ficha.
- Contagem persistente no MySQL com reserva atômica diária e mensal antes da chamada. Tentativas que falham também consomem reserva. A migração `034_free_tier_usage.sql` cria a tabela; não armazena pergunta, resposta ou chave.
- Pausa administrativa continua bloqueando IA. Chamadas gratuitas confirmadas podem funcionar com orçamento monetário zero; o caminho pago continua bloqueado com orçamento zero.
- GET com consulta e POST da Cuia passam pelo limite existente de requisições.

## Configuração sem segredos versionados

Use `dotnet user-secrets` no desenvolvimento ou o cofre/variáveis do ambiente na implantação. Não cole chaves em relatórios, comandos compartilhados ou appsettings versionado. Revogue primeiro os segredos anteriormente expostos no chat.

Configurações não secretas para **depois** de verificar as contas:

```json
{
  "Cuia": {
    "FreeTier": {
      "ConfirmedFreeProviders": ["Groq", "Gemini"],
      "DailyRequestLimit": 100,
      "MonthlyRequestLimit": 1000
    }
  },
  "WebSearch": {
    "Tavily": {
      "Enabled": true,
      "FreeTierConfirmed": true,
      "DailyRequestLimit": 30,
      "MonthlyCreditLimit": 900
    }
  }
}
```

Configure separadamente os segredos `Ai:Groq:ApiKey`, `Ai:Gemini:ApiKey` e `WebSearch:Tavily:ApiKey`. Variáveis .NET equivalentes usam `__` em lugar de `:`, por exemplo `WebSearch__Tavily__ApiKey`. Informe apenas os provedores efetivamente verificados em `ConfirmedFreeProviders`.

No controle administrativo dos provedores Gemini/Groq, a pausa precisa estar desativada para permitir chamadas. Não aumente o orçamento monetário para liberar uso gratuito; ele pode continuar zero. O orçamento monetário local permanece zero; a pausa local do Gemini foi verificada como desativada.

Aplique as migrações pelo fluxo normal do projeto antes de ativar. Cota indisponível ou falha no banco não libera chamadas. Reservas usam UTC e são compartilhadas pelas instâncias que usam o mesmo banco. Outros projetos ou serviços usando a mesma conta/chave também consomem a cota do provedor e não são contabilizados aqui. Separe as contas/chaves e mantenha margem; respeite os limites de tokens e os erros 429 do serviço.

## Rotas, mapa e imagens

IA e busca não calculam itinerários confiáveis por conta própria. [OpenTripPlanner](https://docs.opentripplanner.org/en/latest/) é software aberto para planejamento multimodal usando GTFS/OSM; exige infraestrutura e dados vigentes. Não foi encontrado nem homologado um feed local nesta entrega. Não há nova cobertura comprovada de ônibus, BRT, barcos, balsas ou avião.

Leaflet já está no catálogo. Dados OSM abertos não significam tiles públicos ilimitados: respeite a [política da OSMF](https://operations.osmfoundation.org/policies/tiles/). Resultados do Google permanecem sujeitos aos termos do Google. Fotos reais exigem origem e licença; não use uma imagem gerada como fotografia documental do Pará ou de um estabelecimento.

## Verificação e limites desta entrega

Os testes novos cobrem fallback de provedor, ausência de conta gratuita confirmada, cancelamento, ausência de recomendações, consentimento da busca, contrato JSON/texto, quota esgotada, falhas HTTP e fontes inseguras. A integração com MySQL verifica reservas concorrentes, persistência entre escopos e pausa administrativa com orçamento zero.

`scripts/verify-free-search.cjs` verifica a página real com respostas HTTP simuladas: opção desligada inicialmente, pesquisa solicitada, links, pergunta sobre ônibus sem interceptação pelo atalho local, axe e texto a 200% nas larguras 320/390/768/1440.

Na Sprint 46 não foram feitas chamadas reais. Na Sprint 47 houve geração real de texto e JSON pelo Gemini; o relatório distingue esse teste da integração de ponta a ponta. Groq e Tavily não foram homologados com serviços reais. Não foram instalados Ollama/OTP, criadas contas, contratados planos ou alteradas restrições de cobrança. Não houve publicação de fichas, push ou deploy.

Resultado local: build Release sem avisos/erros; 317 testes unitários aprovados; 17 testes de integração pertinentes aprovados (Cuia HTTP e quotas). O teste concorrente encontrou inicialmente um deadlock na promoção de bloqueios com `INSERT IGNORE`; a reserva passou a usar upsert com bloqueio exclusivo na ordem mês → dia e os testes passaram. Nenhuma chamada externa é feita por esses testes.

Interface: oito combinações de página/largura verificadas e quatro cenários de pesquisa com texto a 200%, sem overflow nem falhas detectadas no escopo automatizado. Axe foi executado na largura 320; as demais larguras tiveram verificações de layout e contrato. Isso não certifica WCAG integral nem qualidade de respostas reais. Evidências locais ignoradas pelo Git: `artifacts/sprint46-build-final.log`, `artifacts/sprint46-unit-final.log`, `artifacts/sprint46-integration-final.log`, `artifacts/sprint46/search-ui/report.json` e `artifacts/sprint46/cuia-pages/report.json`.

Reprodução:

```powershell
dotnet build BelemRestaurantes.sln -c Release
dotnet test tests/BelemRestaurantes.UnitTests -c Release --no-build
dotnet test tests/BelemRestaurantes.IntegrationTests -c Release --filter 'FullyQualifiedName~FreeTierQuotaTests|FullyQualifiedName~CuiaAiEndpointTests'
# Depois de iniciar o servidor local Development em localhost:5180:
node scripts/verify-free-search.cjs
```

As alterações desta entrega ficaram locais, sem novo commit. A inspeção dos arquivos alterados não encontrou os padrões de credenciais Groq, Google ou Tavily verificados; credenciais não foram incluídas no código ou em arquivos versionados. O Gemini foi salvo somente no cofre local de desenvolvimento por autorização do usuário.

## Limites por sessão na Sprint 47

Configuração local: Gemini confirmado gratuito, `DailyRequestLimit=20`, `MonthlyRequestLimit=200`, `SessionTokenLimit=24000`, `MaxOutputTokens=800`. A política padrão do repositório continua sem provedores gratuitos confirmados; implantações precisam configurar seus próprios segredos e cotas.

O histórico enviado tem até duas perguntas de 300 caracteres, até três trechos de fonte de 700 caracteres e até quatro lugares publicados. Antes de cada tentativa, a reserva usa bytes UTF-8 do pedido serializado, mais o teto de saída e uma margem de 256 unidades. É uma estimativa conservadora, não uma medição exata pelo tokenizer. Falhas também consomem reserva. O cookie de sessão é protegido, HttpOnly e válido por 24 horas; HTTPS acrescenta Secure. Limpar a conversa não reinicia esse orçamento. Limpar cookies ou usar outro navegador pode criar outra sessão; as cotas globais persistentes continuam valendo. Esses limites não substituem a cota nem a configuração de cobrança do Google.

Os contadores anônimos não guardam conversas, IP ou chaves. Uma limpeza limitada a 100 linhas por reserva elimina sessões sem uso há mais de dois dias. O controle administrativo de pausa continua prevalecendo.

## Atualização da Sprint 48

O Worker também usa reservas gratuitas. No ambiente local, os limites por finalidade são `WorkerDailyRequestLimit=2` e `WorkerMonthlyRequestLimit=20`, além do teto da conta compartilhada de 20/dia e 200/mês. Web e Worker reservam no mesmo serviço `cuia_gemini`; o contador `worker_gemini` é a restrição adicional por finalidade. Falhas consomem tentativas. Configurar chave e modelo separadamente no cofre do Worker não habilita provedores pagos; o orçamento monetário continua zero e pausas continuam prevalecendo.

A chamada .NET real desta Sprint ao Gemini retornou 503, sem geração. A resposta sobre o Na Maré foi homologada usando referência editorial real revisada e indexada, com ressalva sobre cardápio atual, sem depender de geração. Isso não é pesquisa aberta em tempo real. Busca aberta continua aguardando conta/chave; grounding pago não foi ativado. O teste único com `gemini-2.5-flash` retornou 404 e não virou provedor alternativo.

Diagnóstico, evidências, migrações e pendências atuais: [SPRINT48-REPORT.md](SPRINT48-REPORT.md). Execução contínua de desenvolvimento: [LOCAL-WORKER.md](LOCAL-WORKER.md). Os resultados anteriores neste documento permanecem como histórico das respectivas Sprints.


## Sprint 50: conhecimento público sem chave

Foi habilitado no cofre local do Web `WebSearch:PublicSources:Enabled=true`, com 30 tentativas/dia e 300/mês. Outros ambientes precisam optar explicitamente. Na ausência de Tavily, a pesquisa usa somente Wikipédia em português, com cache de 30 minutos, atribuição e prazo de 7 segundos; não é busca geral nem consulta de cardápio atual. Falhas não consomem tokens de geração, mas contam na cota de tentativas de pesquisa. Houve sucesso real seguido de falhas de conexão na homologação. Não usar sondagens generativas como monitoramento. Detalhes e limitações: [SPRINT50-REPORT.md](SPRINT50-REPORT.md).


## Sprint 51: busca geral e evidências

Tavily e o novo adaptador Brave exigem credencial e confirmação explícita de franquia gratuita. Nenhum está configurado localmente. Brave: `WebSearch:Brave:Enabled`, `FreeTierConfirmed`, `ApiKey`, `DailyRequestLimit` (20), `MonthlyRequestLimit` (200). Não habilitar com cobrança presumida. O cache conserva datas e não há desvio automático de erro/quota para outro provedor. `Research:FetchPages=false` desativa recuperação de páginas, preservando trechos de pesquisa. Google/Bing não foram ativados: consulte as restrições atuais em [SPRINT51-REPORT.md](SPRINT51-REPORT.md). Na homologação real, o Gemini ficou sem cota e as respostas usaram referências; não aumentamos o orçamento.
