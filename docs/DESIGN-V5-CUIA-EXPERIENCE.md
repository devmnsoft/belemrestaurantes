# Design V5 — Cuia Experience | Belém Restaurantes
## Direção de produto
Guia gastronômico paraense fotográfico, popular, editorial, responsivo e acessível. Não se parecer com dashboard SaaS na área pública. Hierarquia clara: descobrir restaurante > ver fotos reais > horário e preço verificáveis > chegar > avaliar/compartilhar.

## Identidade
- Marca original Cuia: tipografia legível, símbolo minimalista de cuia e folha, sem uso de estrelas Michelin.
- Base: verde profundo #113F32; tucupi #E8AD46; urucum #B7513C; palha #F7F4EB; texto #17342D.
- Tipografia com escala consistente, sem dependência de emoji ou ícones decorativos sem significado.
- CSS tokens e componentes reutilizáveis; eliminar estilos inline após migração por tela; arquivos antigos removidos apenas depois dos testes visuais.
- Imagens reais dos pratos, restaurantes e experiências autorizadas em primeiro plano; placeholders identificados, sem fotos de estabelecimentos errados.

## Jornadas obrigatórias
1. Visitante: Home > busca autocomplete > filtros > restaurante > fotos/cardápio/horário > rota/ônibus > experiência/WhatsApp.
2. Membro: cadastro/email > login > avaliação/foto > privacidade > moderação > compartilhamento > edição.
3. Parceiro: reivindicar > comprovar vínculo > aprovação > editar perfil/cardápio/fotos/horários > responder > plano.
4. MNSOFT: descoberta > deduplicar > aprovar > media > reclamações > IA > conectores/custos > auditoria.
5. Mobile: navegação simples por até cinco destinos; menus expansíveis, foco WCAG 2.2 AA; CTAs não sobrepostos.

## Páginas e entregas
- Home editorial com categorias, seleção geográfica, destaques confiáveis e receitas.
- Busca por prato/restaurante/bairro/cidade sem conteúdo falso e com estados vazios.
- Detalhes: capa, galeria licenciada, botão grande 'Como chegar', horários com última atualização, compartilhamento e cuia.
- Comunidade: feed com imagens dos usuários, autores autorizados e estados de privacidade.
- Parceiro: navegação por tarefa, passos curtos, validação e mensagens claras.
- Admin: sidebar profissional, tabelas acessíveis, indicadores reais e recursos administrativos protegidos.

## Segurança
- Nenhuma imagem externa vinculada sem licença e identidade conferidas.
- URLs externas allowlist/validadores; evitar scripts externos desnecessários; CSP sem inline inseguro.
- Não expor dados pessoais ou fotos privadas em OpenGraph, cache ou DOM público.
- Testar todos os layouts em 360/390/768/1024/1440px, zoom 200%, teclado, leitores de tela e contraste.
- Evitar declarar "guia oficial" sem vínculo com órgão público.

## Critérios de aceite
Build Release, testes de UI e rotas, não regressão de autorização, sem botões inertes, dados reais ou estados vazios honesto, design consistente nas páginas públicas e administrativas, comparativos visuais antes/depois, sem CSS fragmentado no final.
