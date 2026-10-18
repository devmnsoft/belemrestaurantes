# Sprint 25 — catálogo, imagens, leitura e vídeos

Pedido adicional de 18/10/2026: corrigir o erro dos logs e evoluir imagens por tipo de restaurante, acessibilidade para baixa visão, design, vídeos e aprovação em massa.

## Erro corrigido

`/admin/restaurants` selecionava `price_range`, `state` e `cuia_score` como se fossem colunas de `restaurants`. O esquema versionado usa `price_min`, `price_max`, `status` e a tabela `cuia_rankings` para as notas. Corrigida a projeção SQL; ausência de nota permanece ausência, sem nota inventada. Corrigidos também paginação com limites, rótulos dos filtros e o link público, que apontava para uma rota inexistente.

O teste HTTP da tela administrativa passou no MySQL local, inclusive com número de página excessivo. Não foi necessário adicionar colunas ou migrar dados para contornar a consulta incorreta.

## Imagens e design

- Nove artes SVG originais: restaurante, açaí, pizzaria, lanchonete, cafeteria, padaria, sorveteria, bar e peixes/frutos do mar.
- Seleção por categoria, com normalização de acentos; usadas nos cards e na página do restaurante quando não há foto.
- Identificação visível como **ilustração de categoria**; fotos existentes têm prioridade. As artes não representam fotografias dos estabelecimentos e não alteram `photo_url` nem direitos das mídias existentes.
- Composição com verde amazônico, palha, dourado, formas de rio e referências à mesa, incorporada às views e ao CSS já existente. Não foi adicionada uma nova folha global de sobreposições.

## Leitura para baixa visão

Controle **Conforto de leitura**, no topo do portal e da administração: texto padrão/maior/bem maior, alto contraste, links destacados, redução de movimento e restauração. Preferências persistem somente no navegador; a função continua disponível quando o armazenamento está bloqueado. Estado anunciado a leitores de tela, controles nativos com rótulos e foco com dois anéis de contraste. A preferência de movimento reduzido do sistema também é respeitada.

Essas melhorias não equivalem a certificação WCAG. Falta revisão visual em navegador, teclado e leitor de tela nos tamanhos 360, 390, 768, 1024 e 1440 px: a ferramenta de interface desta sessão retornou inventário sem navegadores. Nenhuma captura visual foi apresentada como validação executada.

## Vídeos

- Catálogo administrativo: **Gerenciar vídeos** abre `/admin/midias?restaurantId=<id>`.
- Editor cadastra link de um vídeo, título/autoria e fonte; deve confirmar que conferiu sua relação com o restaurante. Novo conteúdo fica pendente.
- Moderador/administrador aprova ou retira; submissão e decisão têm auditoria transacional e antiforgery. Editor não aprova.
- Lista de plataformas e formatos permitidos: vídeos do YouTube, Instagram, TikTok e Globoplay. Perfis, esquemas inseguros e domínios semelhantes maliciosos são recusados.
- Somente vídeos aprovados aparecem na seção pública. YouTube usa incorporação `youtube-nocookie` após clique; as outras plataformas abrem a fonte. Sem reprodução automática, iframe antecipado ou HTML de incorporação vindo do banco. Vídeos foram separados da galeria de imagens, onde antes seriam exibidos como imagens quebradas.
- Homepage ganhou destaque para [Receitas É do Pará — Tacacá, TV Liberal](https://globoplay.globo.com/v/12465126/), já existente nas referências editoriais e reconferido na página da fonte nesta execução. Link para reprodução no Globoplay e para a adaptação editorial da receita no portal. Não foi atribuído a um restaurante.

Não foram cadastrados vídeos não verificados em estabelecimentos reais. O teste de publicação usa um endereço fictício explicitamente como fixture, sem reprodução externa, e remove seus registros ao final. A confirmação de relação e autoria continua sendo tarefa editorial humana.

## Aprovação em massa

- Padrão: **1.000 registros por confirmação**, teto de **2.000** por configuração `Moderation:BatchLimit`.
- Exibir 30, 60 ou 120 registros por página; filtros e tamanho preservados na navegação.
- A seleção do filtro pega os **próximos registros até o limite**, seguindo a ordem escolhida, em vez de recusar o filtro inteiro quando ele ultrapassa o limite. A confirmação mostra a lista exata, imutável e com expiração.
- Após aprovar/publicar, o filtro de pendentes permite pegar o próximo lote. Registros incompletos ou com conflitos continuam sujeitos à validação individual; o filtro “Elegíveis para publicação” ajuda a priorizá-los.
- Preservados: confirmação explícita, permissões, revalidação transacional, auditoria por registro e idempotência. Formulários e coleção de IDs suportam o teto de 2.000.

Nenhum lote real foi aprovado automaticamente nesta execução. Os testes exercitam publicação somente em fixtures isoladas e removidas ao final.

## Validação e entrega

- Build Release da solução: aprovado, zero avisos e erros no build final.
- Integração selecionada: **49 aprovados, 1 teoria ignorada**, cobrindo portal/mídias, lote, permissões, antiforgery, login/MFA e migrações. A teoria opcional de contas reais foi corrigida para aparecer como ignorada quando desabilitada: antes seus três casos retornavam silenciosamente e eram contados como sucesso, sem executar a verificação.
- `node scripts/test-reading-tools.cjs`: aprovado; tamanhos, persistência, restauração, contraste, anúncio e falha de armazenamento.
- Testes de integração verificaram também envio de 2.000 IDs, limitação de seleção por filtro, próximo lote, vídeo pendente versus aprovado, separação da galeria e rejeição de links inseguros.
- Testes unitários: **255 aprovados**.
- Migrações existentes reutilizadas; nenhuma nova migração.
- Alterações pré-existentes nos dois `appsettings.json` foram preservadas e não fazem parte dos commits desta entrega. Sem push ou deploy.

Arquivos principais: `AdminOperationsController`, `AdminMediaController`, `MySqlBulkModeration`, `BulkModeration`, `CategoryArtwork`, `RestaurantVideo`, views do catálogo/moderação/restaurante/homepage, parciais de leitura/arte/vídeo, `para.css`, scripts de leitura/vídeo/lote e `PortalMediaTests`.

Limites restantes: revisão visual e de tecnologias assistivas, inclusão editorial de vídeos específicos de cada restaurante e fotos reais autorizadas onde faltam. A verificação pessoal da senha atual do superadministrador discutida na conversa não foi substituída por esta entrega.
