# Sprint 52 — Referências externas compartilhadas pelo portal

Entrega local em 20/10/2026. Implementa o pedido de coletar informações, links de matérias e vídeos e alimentar o site, sem republicar o corpo dos posts. Sem commit, deploy, novas contas ou serviços pagos.

## Fluxo implementado

O crawler e a pesquisa recorrente alimentam `external_content`, biblioteca persistente compartilhada por Home, Descobrir, `/referencias` e Cuia. Os dados publicados são título, origem, URL canônica, resumo próprio do portal, datas, link do vídeo e campos estruturados disponíveis (nome, endereço, menções de pratos e horários). O corpo da matéria e a imagem `og:image` não são armazenados nem republicados.

A biblioteca pública final contém cinco referências pertinentes (ver `artifacts/sprint52/public-content.json`). A biblioteca começa com referências anteriormente aprovadas e recebeu duas referências públicas identificadas na pesquisa desta entrega: uma página de vídeo da TV Brasil sobre culinária paraense e uma página do Ministério do Turismo sobre Belém. A importação foi de metadados e links; não de texto integral ou mídia. Evidência: `artifacts/sprint52/metadata-import.log`.

Novas referências válidas são publicadas automaticamente, sem aprovação por item. URLs de rastreamento são normalizadas para reduzir duplicatas. Recoleta preserva campos factuais anteriores quando a nova observação não contém dados estruturados. A publicação tem auditoria; suspensões também impedem que o mesmo URL volte à Cuia por outra busca ou pelo índice de referências; a suspensão administrativa remove o item da recuperação pública e pela Cuia, e uma recoleta não o republica. Controle em `/admin/conteudos-externos`, com política administrativa, MFA e antifalsificação existentes.

A recuperação pela Cuia distingue `collected_sources`: acervo anteriormente coletado, não pesquisa nova ao vivo. Perguntas de acompanhamento mantêm a entidade anterior e restringem referências ao lugar identificado. O teste de integração verifica uso de um prato documentado no acervo sem consulta externa ou geração. Informações comerciais continuam acompanhadas de ressalva de atualidade.

## Coleta periódica sem chave de buscador

Foi acrescentado o modo `content_only` nas fontes existentes. Uma fonte RSS/Atom nesse modo publica somente referências pertinentes ao assunto e ao Pará/município da pesquisa; não transforma a reportagem em restaurante e não segue navegação arbitrária para criar estabelecimentos. Metadados do feed podem alimentar o acervo mesmo quando a página não é baixada. Páginas permitidas podem complementar campos estruturados e links de vídeo.

Fontes desse modo são revistas a cada hora, dentro da cadência do crawler, preservando pausas, limites, proteção SSRF e regras robots. O formulário `/admin/crawler` permite cadastrar uma fonte somente de referências.

O endereço antigo de cultura da EBC retornou HTTP 500. O feed atual oficial foi lido com sucesso: `https://agenciabrasil.ebc.com.br/rss/ultimasnoticias/feed.xml`. Retornou 10 itens. Dois passaram pelo filtro inicial amplo, mas a revisão identificou notícias gerais sem pertinência suficiente. O filtro foi corrigido para exigir assunto no título; as duas referências foram suspensas com auditoria e não representam descobertas válidas. Fonte cadastrada: ID 31, ativa, `content_only=true`. Evidência real: `artifacts/sprint52/feed-ebc-current.log`. A coleta ficou configurada para detectar futuras publicações pertinentes, sem inventar resultados quando o lote não contém assuntos do portal. Itens nacionais sem pertinência regional são descartados; “para” como preposição não é tratado como o estado Pará.

O feed da Agência Belém não foi habilitado após falha de confirmação de acesso. Não se declara cobertura de todas as revistas, redes sociais ou de toda a internet. A busca geral continua dependendo de um provedor configurado; RSS e fontes diretas não exigem essa chave.

Diagnóstico de conectividade: o host EBC respondeu por IPv4 em cerca de 0,44 s, enquanto IPv6 atingiu o prazo de 5 s. O cliente seguro agora prefere IPv4 entre os endereços previamente validados e mantém alternativa IPv6; não desativa IPv6 na máquina nem remove proteção DNS/SSRF. A cadência publicada de 10 segundos da EBC é respeitada. Falhas de robots agora registram diagnóstico de host, status e motivo.

## Prévia e PNG

Cada cartão possui uma prévia SVG criada pelo portal, sem imagem ou recurso externo. O botão “Baixar prévia em PNG” captura esse cartão no navegador e exporta a imagem. Não é screenshot da página original, nem fotografia do estabelecimento. A identificação fica visível no cartão e na própria imagem.

O link do vídeo abre a página ou o player indicado pela fonte. São reconhecidos metadados Open Graph, `VideoObject` JSON-LD e links públicos de vídeo em feeds. Não há cópia ou download do vídeo, nem login em plataformas privadas. Imagens e capturas de páginas originais exigem uma integração e condição de uso apropriadas; não foram simuladas.

## Dados e operação

Migração 041: coluna `crawler_sources.content_only` e tabela `external_content`, com índice por URL, município, estado e data. Importação inicial usa somente referências aprovadas, ignora fontes de teste e não altera restaurantes ou conteúdo anterior. O runner conserva a migração aplicada, sem sobrescrever edições em novas leituras.

Ferramentas locais:

- `--reference-feed=<URL HTTPS> --city=Belém`: valida origem, robots e RSS/Atom antes de cadastrar fonte ativa somente de referências; preserva configurações e pausas se já existir.
- `--import-reference-metadata <JSON>`: importa até 20 metadados por lote de até 128 KiB, com validação de links e assunto. O coletor gera o resumo próprio; o texto recebido não vira uma matéria republicada.

Os limites gratuitos e a cota local de IA foram preservados. Esta entrega não depende de geração para coletar feeds, publicar referências ou produzir a prévia. Nenhuma chave foi adicionada ao código.

## Validação

- Build Release: zero erros e avisos.
- Unitários: 382 aprovados.
- Integração: 216 aprovados e um teste preexistente ignorado.
- Cobertos: normalização/deduplicação, associação de vídeo, filtro regional, publicação automática, preservação de fatos na recoleta, suspensão persistente e auditada, alimentação da Cuia, rotas públicas e autorização administrativa, escape da prévia e ausência de mídia externa copiada. Após o ajuste final de pertinência dos títulos, foram repetidos 10 testes de coleta e regressões de referências/fila, todos aprovados (`integration-focused-final.log`).
- Evidências em `artifacts/sprint52`. A conferência visual e o download PNG são registrados em `ui/report.json` e `ui/reference-preview.png`. No WebKit, o Axe produziu um falso positivo de cor de fundo do rodapé: a captura e os estilos mostram fundo verde, mas a ferramenta inferiu outro fundo. Para essa região, os demais critérios Axe foram mantidos e o contraste foi validado separadamente pelos valores de cor computados, com piso de 4,5:1. A cor de fundo do seletor de idioma também ficou explícita para evitar diferenças de aparência nativa no mobile. Essa exceção está explícita nos registros `footerContrast`.

Os testes automatizados permanecem isolados de chamadas pagas e de chaves reais. A leitura do feed e a importação de metadados públicos estão documentadas separadamente.

## Fontes públicas consultadas

- [RSS oficiais da Agência Brasil](https://agenciabrasil.ebc.com.br/feed/).
- [Conheça mais sobre a culinária paraense — TV Brasil](https://tvbrasil.ebc.com.br/reporter-brasil/2026/10/conheca-mais-sobre-culinaria-paraense).
- [Belém: Cidade Criativa da Gastronomia — Ministério do Turismo](https://www.gov.br/turismo/pt-br/mtur-na-cop30/belem-pa-cidade-criativa-da-gastronomia).

A versão local fica disponível em `http://localhost:5180/referencias`. No Visual Studio, encerre a execução anterior, recompile e reinicie o perfil Web para carregar as alterações.


## Estado final verificado

27 cenários visuais e mobile concluídos, sem overflow e sem violações nos critérios e métodos documentados no relatório de interface. O PNG foi baixado e validado pela assinatura do arquivo, além da inspeção visual. O teste WebKit mede contraste do rodapé separadamente; o mínimo registrado foi superior a 4,5:1.

Worker retomado: PID 37800, supervisor 52888. Processo e heartbeat recente confirmados em `artifacts/sprint52/worker-final.json`. O feed ID 31 permanece ativo no modo somente referências. Não houve novo commit.
