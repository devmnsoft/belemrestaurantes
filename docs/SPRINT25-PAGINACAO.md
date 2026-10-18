# Sprint 25 — leitura e paginação da aprovação

Atualização de 18/10/2026 para `/admin/moderacao` e a caixa de revisão de `/admin/discovery`.

## O que mudou

- Navegação no início e no fim da lista: primeira, anterior, páginas próximas, próxima, última e campo para ir diretamente a uma página. Filtros e tamanho de página são mantidos.
- Faixa de resultados visível, por exemplo “31–60 de 25.158”, com indicação da página atual.
- Seleção acompanha as páginas do mesmo filtro na mesma aba, inclusive ao mudar a ordenação ou entre 30, 60 e 120 registros por página. IDs selecionados em outras páginas são enviados ao preparar o lote e contabilizados na tela. Limite do lote continua aplicado no cliente e no servidor.
- A seleção é separada por filtro e guardada em `sessionStorage`, somente com os IDs. Ao preparar a confirmação, ela é limpa; a lista imutável do lote permanece no banco. Cancelar retorna à revisão sem uma seleção antiga oculta. Sem armazenamento permitido, há aviso; sem JavaScript, a revisão funciona uma página por vez.
- Confirmação e resultado mantêm o caminho de volta aos filtros. O caminho de retorno é restrito à central de moderação, sem aceitar destinos externos.
- Aprovação/rejeição individual no Discovery agora mantém filtros e página e retorna ao trecho dos cadastros. Atalho no topo evita percorrer os relatórios do Worker para alcançar a revisão. Transferência para aprovação em lote mantém o filtro utilizado.
- Leitura: fonte base de 16 px, espaçamento maior, uma linha de revisão por restaurante, situação em português, dados essenciais à vista e informações secundárias em “Mais informações”. Filtros avançados ficam recolhidos quando não utilizados. Resultados priorizam os registros que exigem atenção. Controles com área mínima de 44 px e compatibilidade com as preferências de leitura existentes.
- Mensagens de carregamento e bloqueio do reenvio enquanto o lote está sendo preparado. Voltar/Avançar do navegador restaura o estado da seleção e do botão.

## Consultas e índices

A central agora conta os registros usando somente os filtros necessários, seleciona os IDs da página e só depois busca observações, origem e duplicidade desses IDs. Filtros de fonte, duplicidade e elegibilidade continuam sendo calculados **antes** de paginar quando solicitados. A mesma ordem é usada na seleção de lote. O ID desempata nomes e datas iguais.

No Discovery, a agregação de toda a fila de moderação foi substituída pela consulta dos conflitos somente dos IDs exibidos. Números de página fora da faixa são ajustados após a contagem, antes de calcular o deslocamento; isso também evita overflow com `page=2147483647`.

Migrações novas, aplicadas e verificadas no banco local:

- `028_moderation_paging`: índices `(status,moderation_stage,created_at,id)` e `(status,moderation_stage,name,id)`.
- `029_discovery_review_paging`: índice `(status,created_at,id)` para a caixa de entrada.

Nenhuma migração anterior foi editada. Em outros ambientes, aplicar as migrações pelo procedimento existente (`dotnet run --project src/BelemRestaurantes.Web -- --migrate` com a configuração do ambiente correto) e reiniciar/recompilar a aplicação. A implantação em IIS/serviço não foi executada nesta etapa.

## Medições locais

Filtro padrão da central com **25.158 pendentes**, 30 itens por página. Tempo de `ListAsync`, incluindo conexão e consultas; não é tempo de renderização do navegador. Amostras de desenvolvimento, sem ensaio estatístico de carga:

| Ordenação / página | Antes | Depois, duas amostras |
|---|---:|---:|
| Mais antigos / 1 | 49,1 ms | 12,5 e 15,6 ms |
| Mais antigos / 100 | 250,1 ms | 62,1 e 100,6 ms |
| Nome / 1 | 56,9 ms | 17,4 e 24,3 ms |
| Mais recentes / 1 | 67,1 ms | 13,3 e 46,0 ms |
| Município / 1 | 124,5 ms | 63,5 e 144,0 ms |

Há variação de aquecimento e concorrência local; a segunda rodada otimizada coincidiu com testes unitários. Não há garantia de redução uniforme para todas as ordenações ou em produção. O `EXPLAIN` da página padrão confirmou `ix_restaurant_review_date`, com `Using where; Using index`.

## Validação

- Build Release da solução, sem avisos ou erros.
- Integração selecionada: **67 aprovados e 1 teoria opcional ignorada**. Inclui publicação, permissões, antiforgery, login/MFA, migrações e os novos cenários de paginação.
- Teste com 34 fixtures: páginas sem repetição, desempate por ID, limite de tamanho, páginas extremas, fontes mais recentes, duplicidade, elegibilidade, aprovados/publicados, preservação do retorno e rejeição de retorno externo. As fixtures e seus lotes/auditorias foram removidos ao final.
- `node scripts/test-bulk-selection.cjs`: seleção entre páginas, IDs fora da página, limites, isolamento por filtro, limpeza ao preparar, Voltar do navegador e armazenamento indisponível.
- `node scripts/test-reading-tools.cjs`: preferências de leitura preservadas.
- Testes unitários: **255 aprovados**. A verificação da quantidade de migrações foi atualizada de 27 para 29, mantendo a validação de sequência e conteúdo.

Não houve aprovação de lotes reais nesta execução. As alterações pré-existentes dos dois `appsettings.json` foram preservadas e ficaram fora dos commits. A revisão visual em navegador e com leitor de tela continua pendente; os testes de interface desta etapa verificam respostas HTML e comportamento JavaScript em ambiente simulado.
