# Sprint 50 — Cuia, Travessias e conteúdo editorial

Entrega local em 20/10/2026. Sem commit, publicação, contratação ou inclusão de credenciais no repositório. Alterações anteriores foram preservadas.

## Pesquisa e respostas

Foi implementada uma composição de pesquisa: Tavily quando configurado e confirmado gratuito; na ausência dessa configuração, consulta limitada à API pública da Wikipédia em português. Não é um buscador geral da internet. A habilitação local foi feita no cofre do Web, com teto de 30 tentativas/dia e 300/mês, sem chave nova. Outros ambientes permanecem desabilitados por padrão. Configuração: `WebSearch:PublicSources:Enabled`, `DailyRequestLimit`, `MonthlyRequestLimit`.

O cliente consulta somente um endpoint fixo, não segue redirecionamentos, limita resposta a 128 KiB, usa prazo total de 7 segundos, separa chamadas, conserva atribuição/data e mantém cache limitado a 128 entradas por 30 minutos. Falhas têm cache curto. Cotas persistentes também contabilizam tentativas sem sucesso. Não há desvio das cotas ou falhas do provedor principal.

Homologação real: `artifacts/sprint50/public-search-live.log` documenta HTTP 200 e três referências sobre Combu, em aproximadamente 2,13 segundos. Porém, consultas posteriores falharam no prazo de conexão, inclusive no CLI (`public-search-final.log`). A prova pelo navegador (`search-browser-live.json`) retornou HTTP 200 nas duas perguntas, `indexed_sources`, uma referência editorial e `evidence_limited`. Portanto, a integração existe e respondeu ao vivo, mas a disponibilidade externa neste ambiente foi intermitente; não se declara busca aberta plenamente homologada.

Perguntas reconhecidas como cardápio usam apenas itens de referências revisadas. Sem cardápio verificável, a resposta explica a lacuna e oferece próximos passos. Trechos de pesquisa não viram pratos confirmados. Roteiros publicados podem aparecer como sugestões reais da Cuia. A classificação de intenção ainda é heurística; não se promete cobertura de todas as formulações.

Estados de pesquisa distinguem ausência de configuração, vazio, falha, quota, cache e índice editorial. Fontes vazias não geram título/lista; títulos/URLs inválidos são descartados. Página, SSR e widget compartilham a apresentação. Markdown trata listas, remove ordinal isolado e escapa HTML. Foram corrigidos cortes nas ações de continuidade.

## Roteiros e Travessias

Migração 039 aplicada localmente. Sete roteiros persistentes e editáveis, com 21 etapas, origem sugerida, público, duração editorial estimada, modalidade, orientações, acessibilidade a confirmar, fontes e data de revisão:

- Mercados e sabores de Belém.
- Cidade Velha sem pressa.
- Docas entre mercado e rio.
- Combu: rio e cacau.
- Cotijuba: um dia de ilha.
- Mosqueiro: vila e paisagem.
- Icoaraci: barro e rio.

A migração usa inserção idempotente e não sobrescreve edição administrativa. Foram adicionadas quatro referências editoriais sem cardápio; não são novos restaurantes publicados. Guias legados vazios deixam de aparecer, sem remoção de registros. GET não recria dados.

`/travessias` apresenta os guias, planejamento e referências mesmo sem tarifas vigentes. `/roteiros` e detalhes permitem guardar todas as etapas no mesmo Meu Passeio, com deduplicação, limite de 20 e reordenação/remoção já existentes. Links de mapas são buscas externas explicitamente identificadas, sem alegar rota calculada, linha, horário ou tarifa confirmada. Os roteiros são uma cobertura inicial de Belém e distritos, não de todo o Pará.

`/admin/roteiros` exige política administrativa com MFA e antifalsificação. Edição salva rascunho; publicação/despublicação são explícitas. Há controle otimista de versão, histórico anterior, auditoria e transação. O Worker verifica a cada hora roteiros publicados revisados há mais de 90 dias e cria pendência única por versão. Isso agenda revisão humana; não verifica automaticamente a atualidade de cada site externo.

Home, Descobrir, Meu Passeio e estados vazios selecionados de busca, cidades, favoritos e comunidade receberam sugestões dos guias publicados. Não foram inventados comentários ou avaliações.

## Imagens e leitura

A imagem principal usa fotografia real existente do Ver-o-Peso, Cayambe, com crédito e link de licença CC BY-SA 3.0. Variantes WebP 480/960, dimensões explícitas e prioridade de carregamento. Legenda fora da fotografia, sem selo sobreposto. Ajustes de quebra de texto, cartões, legendas e ações corrigem estouro em telas pequenas e leitura ampliada. Não foram adicionadas fotos de terceiros sem autorização.

## Validação

- Build Release: zero erros e avisos (`build-final.log`).
- Unitários: 359 aprovados (`unit-final.log`).
- Integração: 210 aprovados e um teste preexistente ignorado (`integration-final.log`).
- UI: 40 cenários, larguras 320/390/768/1440, leitura 200%, fontes/Markdown simulados, Meu Passeio e snapshots administrativos autorizados. Zero violações Axe nos critérios executados, overflow ou erros JavaScript (`ui/report.json`). Isso não substitui auditoria manual integral.
- Mobile: 36 cenários Chromium/Pixel 7 e WebKit/iPhone 13, retrato/paisagem, sem overflow ou erros JS (`platforms/report.json`). Emulação no Windows; não certificação em aparelhos físicos.
- Revisão visual das capturas da Home móvel e Travessias desktop. Capturas em `artifacts/sprint50/ui` e `platforms`. A tentativa de captura anterior na instância Debug 63859 falhou por indisponibilidade; não há comparação antes/depois dessa instância.
- Migração reaplicável, rascunho/publicação/despublicação, conflito de versão, histórico, fila e autorização cobertos por integração. Testes automatizados desabilitam chamadas externas; a prova real está separada.
- `git diff --check` sem erros de whitespace. Sem novo commit.

Worker retomado: PID 44980, supervisor 44480; processo existente e heartbeat recente confirmados em `worker-final.json`. Heartbeats antigos não devem ser interpretados como processos ativos. O supervisor é local e depende desta máquina; nenhum serviço de produção foi instalado.

## Limites e uso

Tavily continua sem chave; Wikipédia não confirma menus, preços, funcionamento ou transportes atuais. Rede externa está intermitente. As cotas Gemini anteriores foram preservadas. A primeira sondagem pelo navegador usou formulação não reconhecida como cardápio e retornou geração indisponível; sondagens finais reconheceram cardápio e não fizeram geração. Não se declara sucesso de geração Gemini nesta Sprint. SMTP permanece pendente conforme a entrega anterior.

Para conferir no Visual Studio, encerre a execução antiga, recompile e reinicie o perfil do projeto Web; o perfil local usa `https://127.0.0.1:63859/`. A revisão desta entrega também ficou disponível em `http://localhost:5180/`. Não é necessário elevar limites de cabeçalhos ou desabilitar autenticação.

## Referências editoriais e técnicas

- [API Search do MediaWiki](https://www.mediawiki.org/wiki/API:Search).
- [Política de User-Agent da Wikimedia](https://foundation.wikimedia.org/wiki/Policy:Wikimedia_Foundation_User-Agent_Policy/en).
- [Ilhas de Belém — Agência Belém](https://agencia.belem.pa.gov.br/ilhas-de-belem-descubra-praias-e-trilhas-perto-da-capital/).
- [Pontos turísticos — Agência Belém](https://agencia.belem.pa.gov.br/belem-tera-11-pontos-de-informacoes-turisticas-em-locais-estrategicos/).
- [Estação das Docas — UFG](https://publica.ciar.ufg.br/projetos/rosa-kliass/obras/pa-estacao-das-docas.html).
- [Mosqueiro — Agência Belém](https://agencia.belem.pa.gov.br/uma-homenagem-pelo-olhar-de-quem-mora-e-faz-historia-na-ilha/).
- [Obras da Orla do Cruzeiro — Agência Pará](https://agenciapara.com.br/noticia/78844/obra-da-nova-orla-do-cruzeiro-avanca-no-distrito-de-icoaraci-em-belem).
- [Fotografia e licença](https://commons.wikimedia.org/wiki/File:Bel%C3%A9m_Ver-o-Peso_01.jpg).

Cada etapa também conserva a própria fonte na base e na página pública. Datas, obras e condições operacionais exigem confirmação antes do passeio; referências históricas não foram convertidas em horários vigentes.
