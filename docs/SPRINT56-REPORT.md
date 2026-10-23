# Sprint 56 — busca, fichas, receitas e Cuia

Coleta e testes locais em 21/10/2026. Sem commit, push ou deploy.

## Diagnóstico

A rolagem travava porque o painel de filtros era `sticky` e mais alto que a janela, sem altura máxima. Os ancestrais (`.para-container`, `main`, `body`) não tinham `overflow` que quebrasse o sticky. No celular, o antigo `details` não devolvia a posição da página. A busca também recriava a lista e perdia o ouvinte de rolagem.

Entidades como `&#x20;` não estavam gravadas nas tabelas conferidas (`restaurants`, `menu_items`, `external_content`, `place_reference_index`). O risco estava na ingestão e na renderização. O codificador HTML do site transforma acentos em entidades numéricas no código-fonte; o navegador mostra o acento. Texto já gravado com entidade aparecia literal se fosse só reescapado.

LA BOCA PIZZARIA BELEM não é uma ficha só. Há unidades publicadas distintas. A da Travessa Três de Maio é o CNPJ 44.117.434/0001-41. A ficha OSM usa outra rua para o número 1680 e não pode emprestar a coordenada como ponto exato. Não há horário, cardápio, foto, prato nem reivindicação aprovada para essas fichas. Agregadores divergem no horário e não foram gravados.

A Cuia ficava no catálogo quando a pergunta era de receita ou definição, e a busca por nome usava a frase inteira. Os conectores pagos de busca geral não estão configurados. Wikipédia e os índices públicos regionais estão habilitados e foram chamados de verdade.

## O que mudou

- Desktop: painel sticky com altura máxima e rolagem própria, sem prender a página (`overscroll-behavior: auto`).
- Celular: botão abre diálogo com aplicar, limpar e fechar. Escape devolve o foco. A rolagem da página trava só enquanto o painel está aberto e volta à posição anterior.
- Filtros atualizam a lista por `fetch`, mantêm a seleção e tiram latitude/longitude da URL. A proximidade fica no cookie HttpOnly `br_near`.
- Voltar da ficha restaura a consulta e a posição salva no `sessionStorage`.
- `PublicText` decodifica entidades na ingestão e nos cartões, sem `Html.Raw` de conteúdo externo.
- Ficha reorganizada em “Para a visita” e “Sobre estas informações”. Coleta ausente não vira “não existe”. Reivindicação só conta pedido aprovado.
- Coordenada sem precisão `rooftop` não vira pino exato.
- Cartão e ficha usam os mesmos campos. Página de busca continua em 24 itens.
- Fila de perfil incompleto a cada ciclo de 60 segundos, no máximo 20, sem duplicar pendente, em execução, concluído nos últimos 7 dias ou falha ainda agendada. Ficha com reivindicação aprovada fica de fora.
- Tabelas de proveniência, pratos, vídeos, ingredientes e ofertas. Vídeos em `/culinaria` como cartão “Assistir na fonte”, sem autoplay e sem miniatura inventada.
- Prato, receita oficial e versão caseira são estados diferentes. A lista de compras fica no navegador e só escala porção quando há quantidade.
- A Cuia consulta conector habilitado, registra trilha sem o texto da pergunta e sem nome de provedor, lista unidades homônimas sem misturar horário, e inclui os vídeos publicados quando a pergunta é de vídeo.

## Migração

`044_sprint56_provenance.sql` aplicada no banco local `belrestaurante`.

Colunas novas em `restaurants`: `phone_secondary`, `coordinate_precision`, `coordinate_source`.

Tabelas: `field_provenance`, `place_dishes`, `culinary_videos`, `normalized_ingredients`, `ingredient_offers`.

## Coleta inicial

| Item | Quantidade |
| --- | --- |
| Vídeos de culinária | 3 |
| Ingredientes normalizados | 3 |
| Ofertas com preço | 0 |
| Pratos de estabelecimento | 0 |
| Reivindicações aprovadas da La Boca (423 e 6340) | 0 |
| Itens de cardápio e horários dessas fichas | 0 |

Ficha 6340, LA BOCA PIZZARIA BELEM, São Brás, Travessa Três de Maio, 1680:

- Preenchido: telefone secundário `9193045153`, já publicado no mesmo CNPJ e ausente no importador antigo.
- Proveniência: endereço e telefones como informados pelo cadastro; conflito de rua com o OSM; horário `not_located`.
- Continua sem fonte suficiente: horário vigente, coordenada, cardápio, pratos, foto, preço.

Ficha 423, La Boca Pizzaria, OSM node 12708166615:

- Corrigido: `coordinate_precision = approximate` e origem do nó. A coordenada não foi copiada para a ficha 6340 e o mapa não marca ponto exato.

Outras unidades publicadas e não fundidas: 3243 Ananindeua, 5128 Castanhal, 12003 Nazaré (outro CNPJ, mesmo telefone principal da 6340). Nomes de sócios do QSA não foram gravados.

## Fontes

- https://minhareceita.org/44117434000141 — cadastro público, situação ativa, endereço da Travessa Três de Maio e dois telefones. Consulta usada na coleta de 21/10/2026. Sem horário.
- https://www.openstreetmap.org/node/12708166615 — tags de 22/06/2026: Avenida Gentil Bittencourt, 1680, São Bráz, CEP 66063-388. Conflito de logradouro com o CNPJ.
- https://www.youtube.com/watch?v=mwusmxyNpbU — Culinária Paraense, 21/08/2021, demonstração de tacacá.
- https://www.youtube.com/watch?v=srYZOADHZOg — Cansei de Ser Chef, 13/02/2020, versão tradicional de tacacá.
- https://www.youtube.com/watch?v=vvXIIWGqLhA — Banco da Amazônia, chef Everaldo Akihyto, 10/10/2026, arroz paraense.

Nenhum desses vídeos é cardápio da pizzaria.

## Pesquisa externa da Cuia

Configuração local, sem expor segredo: conhecimento público e índices de sites públicos habilitados. Busca geral paga e plano de pesquisa nomeado não configurados. Chave de geração de texto presente; a cota da sessão esgotou no meio da conversa.

Oito perguntas seguidas em uma aba, todas com HTTP 200:

1. Horários da La Boca em São Brás: quatro fichas listadas, inclusive Travessa Três de Maio. Texto: não junta horários de unidades diferentes. Estado externo `unavailable`, com quatro referências antigas do acervo que não são o horário da casa. Não marcou aberto agora.
2. Pratos desse restaurante: de novo as quatro unidades. Resultados externos descartados como irrelevantes. Nenhum prato inventado.
3. Receita oficial: a frase deixa claro que prato divulgado não é receita oficial e que não há publicação com quantidades e preparo. Cota externa `quota_exhausted`.
4. Vídeos de culinária paraense: os três links do YouTube acima, com autor, plataforma e data, mais referências de enciclopédia que não são esses vídeos.
5. Onde comprar: uma página da Wikipédia sobre culinária mineira. Não é oferta em Belém e não há preço.
6. Quanto custa quatro porções: enciclopédia sem preço. A resposta diz que não há preço verificado.
7. Opção mais barata: uma referência antiga de vídeo, sem preço comparável. O serviço ao vivo não respondeu nessa tentativa.
8. Restaurante ainda fora do catálogo (“Estrela das Onze Janelas”): duas páginas da Wikipédia, uma citando a Casa das Onze Janelas como imóvel histórico, não como restaurante novo. Não foi criado cadastro.

A trilha administrativa fica em `researchNote` (`intencao`, `catalogo`, `decisao`, `estado`, `fontes`, `conector=configurado|nenhum`). O nome do provedor não entra na trilha.

## Testes

- Unitários `Sprint56Tests`: 11 aprovados. Com `CuiaAiTests`, a suíte conjunta anterior ficou em 29 aprovados antes do acréscimo do caso de contexto.
- Web e Worker compilaram com 0 erros. O Web foi recompilado depois do ajuste da Cuia.
- Playwright, Chromium headless, em `http://127.0.0.1:63860`:
  - Desktop 1280×720: painel `sticky`, `overflow: auto`, altura máxima 585px, botão aplicar alcançável, página sem trava de rolagem.
  - Troca de filtro por fetch manteve a cidade na URL e no select.
  - Volta da ficha restaurou `?q=pizza&city=Belém` e a posição 2228px.
  - Celular 390×700: painel abre, trava a página, Escape fecha, devolve o foco e a rolagem (640px para 668px).
  - Paisagem 740×360: ações do filtro dentro da altura visível.
  - Ficha 6340: endereço da fonte, dois telefones, horário ainda não localizado, coleta de cardápio não realizada, responsável não reivindicado, sem pino exato.
  - Ficha 423: coordenada sem precisão de ponto exato.
  - `/culinaria`: três links canônicos, autores, sem `iframe` e sem `video`.
  - Receita `tacaca-em-casa`: adaptação editorial; lista de compras e porções persistidas no navegador (8 porções e item marcado sobreviveram ao reload).

Não há ferramenta de navegador interativo nesta sessão. A verificação de tela foi o Playwright acima, com mouse, toque, teclado e duas orientações.

## Limitações

- O robô enfileira fichas incompletas e ainda não abre páginas novas para completar perfil. Ele não sobrescreve ficha com reivindicação aprovada.
- Não houve geocodificador permitido para a Travessa Três de Maio. A ficha 6340 continua sem coordenada.
- Não há preço, frete ou oferta. Custo de receita não é calculado enquanto faltar preço observado.
- Não há prato identificado da La Boca. Nenhum prato virou receita oficial.
- A Wikipédia entra quando o conector público está ligado e devolve trechos pouco específicos. A resposta precisa ser lida junto com o tipo da fonte. Ela não confirma horário, cardápio nem entrega em Belém.
- A cota de pesquisa e a cota de geração limitam uma conversa longa. Quando isso acontece, a Cuia diz o limite em vez de simular a consulta.
- Nem toda ficha do catálogo foi completada.
