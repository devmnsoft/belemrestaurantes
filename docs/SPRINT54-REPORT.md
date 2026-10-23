# Sprint 54 — Cuia contextual e acesso confiável

Implementação local em 20/10/2026, sem commit, push ou implantação externa. Prompt refinado em `docs/SPRINT54-PROMPT.md`.

## Busca

- Respostas públicas usam exclusivamente a marca **Cuia**. A API pública de status não enumera fornecedores. Logs internos conservam identificação técnica; fontes originais continuam visíveis. O estado da interface informa “A Cuia está pesquisando”, sem simular pensamentos ou progresso fictício.
- Novos conectores para os índices públicos WordPress da Prefeitura de Marituba e do Ideflor-Bio, cujos endpoints retornaram HTTP 200 na verificação. Não exigem chave ou plano pago. Chamadas limitadas a duas origens fixas, com consulta parametrizada, robots, limite de corpo, tempo máximo, quota persistida e validação de que os links permanecem na própria origem.
- Federação existente continua funcionando com os conectores autorizados e cache persistente; o robô também pode usar os novos índices. Não há contorno de autenticação, captchas ou restrições dos sites.
- Perguntas naturais ignoram palavras de preenchimento. Reconhecimento de municípios passou de uma lista de 14 para os 144 municípios oficiais, com correspondência de palavras completas e normalização de acentos.
- Referências persistidas agora podem conter uma descrição factual própria. Quando identificam um lugar, produzem resposta citada e opções de continuação sem depender da geração ou de cota externa. Ambiguidades entre municípios são explicitadas.
- Inseridas duas referências públicas verificadas para “Terra do Meio”: restaurante rural em Marituba, com endereço atribuído à reportagem municipal, e a unidade/região de conservação no Xingu, com referência do ICMBio. A resposta inicial apresenta os dois sentidos, prioriza o contexto gastronômico e a continuação “em Marituba” mantém apenas o restaurante. Não foram inventados cardápio, horários ou preços atuais.

## Acesso

- Diagnóstico somente de leitura identificou: SMTP externo desativado e sem configuração; segredo MFA existente legível; nenhuma conta administrativa bloqueada naquele momento. Nenhuma senha, chave ou segredo MFA foi exibido ou redefinido no diagnóstico.
- Administradores podem entrar com usuário ou e-mail, com espaços externos removidos. Colisões ambíguas são rejeitadas. Cookies, papéis e MFA permanecem separados das contas de membros.
- Membros podem entrar com e-mail ou CPF validado. Confirmação de e-mail e restrições de conta continuam obrigatórias; CPF não concede privilégio administrativo.
- Recuperação administrativa em `/admin/forgot` e `/admin/reset`: token aleatório de uso único, hash no banco, validade de 30 minutos, intervalo entre emissões, CSRF, limitação de requisições, resposta neutra e mensagem na fila criptografada. Troca invalida a versão da credencial e sessões antigas, preservando o MFA. Migração aditiva `043_admin_recovery.sql`.
- Caixa local `/dev/caixa-de-entrada`: habilitada no cofre de desenvolvimento, acessível somente em Development, conexão loopback e Host local. Produção, Host estranho e IP externo são rejeitados. Links usam os fluxos reais de confirmação/recuperação e fragmentos de URL, sem registrar tokens em query strings. Mensagens já esvaziadas ou de outro chaveiro são ignoradas. Não envia e-mails externos.
- Visual Studio abre `https://localhost:63859/`, coerente com o host do perfil. Nenhuma conta real foi redefinida, desbloqueada ou teve MFA desligado automaticamente.

## Como usar localmente

1. Administrador: `/admin/login`, com usuário ou e-mail. Se necessário, solicite a recuperação em `/admin/forgot`, abra a mensagem na caixa local, escolha a nova senha e entre com o autenticador.
2. Novo membro: `/account/register`, abra a confirmação na caixa local e depois entre com e-mail ou CPF.
3. Pesquisa: `/cuia-ia`, pergunte “O que era a Terra do Meio?” e continue pela opção de Marituba.

**Ambiente externo:** é necessário configurar SMTP/TLS e a origem pública corretos para entregar confirmações e recuperações. A caixa de desenvolvimento não substitui e-mail em produção.

## Validação e limites

- Build Release sem avisos/erros. **401 testes unitários aprovados; 223 testes de integração aprovados e um teste preexistente ignorado.** Após o último ajuste de seleção das fontes, os seis testes focados de pesquisa passaram novamente. Logs em `artifacts/sprint54-*.log`.
- Testes de acesso usam contas descartáveis e não consomem o plano real de IA. Cobrem cadastro, confirmação, e-mail/CPF, isolamento de administração, recuperação de uso único, manutenção do MFA e caixa local protegida.
- `scripts/verify-sprint54.cjs`: 15 cenários de páginas de acesso e resposta Cuia em desktop, Android/Chromium e iPhone/WebKit simulados; sem overflow, erros JavaScript ou violações Axe no conteúdo principal. Conversa de duas perguntas verifica desambiguação, município e ausência de nomes de fornecedores.
- As APIs antigas do Bing foram retiradas em 11/08/2025. Google Custom Search JSON API está restrita a clientes existentes e tem encerramento anunciado para 01/01/2027. Não foram apresentados links manuais como consultas automáticas nem adicionados endpoints aposentados.

## Fontes técnicas e factuais

- Microsoft: https://learn.microsoft.com/en-us/lifecycle/announcements/bing-search-api-retirement
- Google: https://developers.google.com/custom-search/v1/overview
- WordPress: https://developer.wordpress.org/rest-api/reference/search-results/
- Marituba: https://marituba.pa.gov.br/site/comida-amazonica-natureza-e-poesia-em-um-so-lugar-restaurante-rural-terra-do-meio/
- ICMBio: https://www.gov.br/icmbio/pt-br/assuntos/biodiversidade/unidade-de-conservacao/unidades-de-biomas/amazonia/lista-de-ucs/esec-da-terra-do-meio/esec-da-terra-do-meio

## Checagem final das fontes públicas

A validação real revelou dois ajustes necessários: o intervalo padrão de cortesia do crawler é de cinco segundos e precisa ser respeitado, e buscas por palavras soltas produziam páginas genéricas. O conector agora suporta esse intervalo, preserva expressões como “terra do meio”, pesquisa a expressão entre aspas e descarta títulos sem correspondência aos termos relevantes. Cache versionado impede reutilizar os resultados anteriores ao ajuste. A disponibilidade automática continua sujeita às regras e à resposta de cada origem.

Uma consulta real por “Floresta Estadual do Iriri” retornou HTTP 200 e a referência do índice público do Ideflor-Bio, além de outras referências, sem usar credencial nesse conector. Evidência em `artifacts/sprint54-public-index-proof.json`. A consulta a “Terra do Meio” continua apoiada nas duas referências factuais persistidas; páginas genéricas foram descartadas.
