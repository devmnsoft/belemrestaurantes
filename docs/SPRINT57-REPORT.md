# Sprint 57 — busca, receitas e pesquisa culinária

Execução local em 21/10/2026. Sem commit, push ou deploy. Credenciais permanecem fora do código.

## Resultado

A busca tem áreas explícitas para filtros e resultados, filtros móveis acessíveis e atualização da pesquisa externa após alterações. Receitas e referências relacionadas aparecem dentro dos resultados, antes do estado vazio de estabelecimentos.

O robô ganhou fila culinária persistente e publica referências de receitas com ingredientes, vídeos vinculados pela fonte e menções de pratos. O conteúdo aparece em `/receitas`, `/culinaria`, `/busca` e `/culinaria/referencia/{id}`. A Cuia usa os mesmos dados e preserva o assunto em perguntas consecutivas.

## Causas corrigidas

- CSS importado antes do tema perdia precedência. O botão móvel podia virar uma célula extra do grid no desktop. A folha específica agora vem depois do tema e as áreas do grid são explícitas.
- A troca de filtros substituía conteúdo sem reiniciar a pesquisa externa. Agora há cancelamento de requisições substituídas, proteção contra respostas atrasadas, atualização dos conteúdos relacionados e suporte ao histórico do navegador.
- O servidor descartava a pergunta inicial ao limitar o contexto às duas últimas perguntas, embora a interface preservasse três. As três perguntas são consideradas.
- Receitas eram limitadas a conteúdo editorial já cadastrado. Foi adicionada ingestão de metadados Recipe em JSON-LD e microdados, sem republicar o texto completo do preparo.
- Páginas em ISO-8859-1/Windows-1252 eram lidas como UTF-8. A codificação declarada agora é respeitada.
- Referências genéricas sobreviviam por coincidências no resumo. A seleção exige termos relevantes; enciclopédias não são usadas para consultas operacionais e precisam corresponder ao assunto no título.
- Uma coincidência entre tema de vídeo e categoria/nome de restaurante podia sugerir vínculo indevido. Na ficha, o vínculo de vídeo agora exige uma receita oficial publicada explicitamente associada ao restaurante.

## Modelo e processamento

Migrações novas: `045_culinary_research.sql` e `046_culinary_search_runs.sql`. O checksum do arquivo 045 aplicado foi preservado; a alteração posterior usa migração separada.

- `culinary_references`: referências publicadas com autoria/fonte, ingredientes, evidência e datas.
- `culinary_tasks`: fila por URL, deduplicação, lease, tentativas, próxima execução e motivo do resultado.
- `culinary_search_runs`: reservas diárias persistentes para descoberta, compartilhadas entre instâncias.
- Vídeos são publicados na tabela existente; a data da receita não é usada como data do vídeo.
- Pratos só são associados a uma ficha quando a identidade local corresponde de forma única. Nome semelhante não funde unidades nem transforma prato em receita oficial.

O ciclo culinário verifica tarefas a cada 60 segundos após terminar o ciclo anterior. Há leases para recuperação e prevenção de processamento simultâneo da mesma tarefa, limites por fonte, política de robots e cliente HTTP protegido contra SSRF. Um ciclo não garante uma novidade publicada.

O painel `/admin/pesquisa-culinaria` exige autorização de moderador e mostra contagens, tarefas, tentativas, estados e falhas.

## Coleta real inicial

Foram persistidas **3 referências de receitas** e **33 menções de pratos** do cardápio público do Remanso do Bosque. São categorias distintas. Ingredientes são os publicados pelas fontes; o preparo completo é acessado na origem.

Fontes efetivamente coletadas:

- https://gourmetadois.com/receita/hamburguer-de-camarao-162.html
- https://receitas.globo.com/regionais/tv-liberal/e-do-para/tacaca.ghtml
- https://receitas.globo.com/regionais/tv-liberal/e-do-para/peixe-e-do-para.ghtml
- https://www.restauranteremanso.com.br/cardapio/

O vídeo do hambúrguer foi identificado nos metadados da página original, sem download, imagem copiada ou reprodução automática. A referência do SaborIntenso foi consultada, mas seus metadados não tinham ingredientes suficientes para publicação pelo extrator; não foi completada por suposição.

## Motor externo e limites reais

Foi mantido o conector geral do plano já autorizado, com verificação de execução de busca e cache persistente. Não foram ampliadas cotas nem contratados serviços. O teste real retornou `quota_exhausted` para a busca geral: a cota local configurada estava esgotada. Isso não foi tratado como pesquisa bem-sucedida.

Os índices públicos existentes foram ampliados com fontes gastronômicas de primeira parte: Combu e Remanso. Resultados continuam condicionados à relevância, disponibilidade, robots e cotas. Essa cobertura dirigida não equivale a pesquisar toda a internet. Google/Bing não foram ativados por endpoints privados ou aposentados.

Há separação entre busca externa ao vivo, cache e referências já coletadas. Uma consulta simples a “tacacá” ainda pode usar conhecimento enciclopédico para definição; perguntas sobre receitas, cardápio, compra e horários não usam enciclopédia como comprovação operacional.

## Caso hambúrguer de camarão

- A grafia “hamburguer de camaração” é normalizada.
- A referência publicada apresenta ingredientes e o link do preparo do Gourmet a dois.
- O vídeo vinculado pelo autor aparece como link.
- A receita não é atribuída ao Remanso ou a outro restaurante de Belém.
- Não foi encontrada evidência suficiente de qual restaurante de Belém vende exatamente esse prato. A Cuia explicita a lacuna.
- Há links identificados como pesquisa manual de ingredientes no Mercado Livre e Amazon. Eles não são ofertas verificadas.
- O extrator suporta preço explicitamente declarado em Product/Offer em BRL, mas a coleta inicial não encontrou cotações suficientes. Não há média de preços nem custo total inventado.
- Marcações da lista de compras ficam no navegador. Não se recalcula rendimento quando a quantidade de porções não é inequívoca.

## Validação

Evidências em `artifacts/sprint57-*.log` e `artifacts/sprint57-ui/report.json`.

- Compilação Release sem avisos/erros. Suítes completas: **425 testes unitários aprovados, 225 testes de integração aprovados e um teste preexistente ignorado**. Os testes automatizados cobrem recuperação de evidência, codificação, correspondência, fila, deduplicação e separação entre receita e restaurante.
- Verificação visual/acessibilidade em 360×780, 390×844 (WebKit), 768×900, 1024×768, 1440×900 e 740×360.
- Cinco perguntas consecutivas no endpoint real, com pesquisa paga desativada nesse teste: prato, receita, vídeo, compra e custo. Verifica autoria, fontes, contexto e nome público Cuia.
- Testes visuais usam resposta externa controlada para conferir que filtros realmente reiniciam a busca sem consumir cota real. A coleta real é validada separadamente.

## Operação

Reinicie o projeto no Visual Studio para carregar os assemblies e folhas atualizados. O servidor de revisão usa `http://localhost:5180`.

O Worker local usa o supervisor existente em `scripts/windows/run-local-worker.ps1`. Sua execução local depende do processo ativo; em produção deve ser operado como serviço supervisionado.

Não foram alterados senhas, MFA, cotas de serviços ou envio de e-mails. As limitações de SMTP previamente documentadas continuam válidas.

## Encerramento operacional

Worker reiniciado e processo confirmado ativo. A busca respondeu HTTP 200 com referência culinária presente após a última compilação. O último ajuste de leitura de preços passou nos 13 testes focados da Sprint 57. Evidências: `artifacts/sprint57-focused-final.log`, `artifacts/sprint57-worker-health.json` e `artifacts/sprint57-culinary-report.json`.
