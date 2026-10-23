# Sprint 49 — cadastro explicável, conversa contínua e imagens resilientes

20/10/2026. Trabalho local em `C:\MNSOFT\belemrestaurantes`, preservando as alterações anteriores. Sem commit, push, deploy, contratação ou aumento das cotas. Nenhuma chave foi incluída no código ou neste relatório. Evidências locais em `artifacts/sprint49`.

## Resultado por área

| Área | Estado comprovado |
| --- | --- |
| Cadastro e validação | **IMPLEMENTADO e TESTADO COM MYSQL REAL**: erros específicos, concorrência/deduplicação preservada e jornada HTTP de cadastro, confirmação e login aprovada. **Envio real de e-mail aguarda SMTP**. |
| Perguntas consecutivas | **IMPLEMENTADO e TESTADO COM SIMULAÇÃO**: quatro perguntas na mesma aba, tanto na página como no widget; recuperação após erros e armazenamento bloqueado. |
| Continuação sobre Na Maré | **HOMOLOGADO NO PORTAL LOCAL** usando a referência editorial real revisada: a segunda pergunta mantém o lugar e orienta confirmação do cardápio. Não depende de geração do Gemini nem representa busca aberta em tempo real. |
| Gemini | **HOMOLOGAÇÃO REAL PARCIAL**: primeira pergunta gerada com sucesso; segunda retornou HTTP 503 do Google e foi substituída por orientação local identificada. Não declarar geração contínua garantida. |
| Imagens e favicon | **IMPLEMENTADO e TESTADO**: favicon público, arquivos locais auditados e contingências de imagem testadas com falhas induzidas. |
| Mobile | Chromium e WebKit emulados, quatro larguras e texto ampliado nos cenários descritos. Não é certificação em aparelhos físicos. |

## 1. Cadastro: causa e correção

O log fornecido mostrava POST 200 que voltava à view `Register`, não uma confirmação de cadastro. A action apagava `ModelState` e substituía falhas de campos por uma mensagem genérica. Não era possível concluir pelo log qual dado o usuário havia preenchido incorretamente; não foram inspecionados documentos ou senhas reais.

Correções:

- Erros de modelo preservados; CPF, CNPJ, confirmação, regras de senha, nome/e-mail e aceite dos termos têm validação e mensagens específicas. Validação complementar também é executada quando uma anotação de propriedade já falhou.
- E-mail é aparado antes da validação. Dados não sensíveis são preservados; senhas e valores tentados de senha são removidos ao reapresentar o formulário.
- Resumo acessível recebe foco, erros aparecem junto ao campo e são associados por `aria-describedby`/`aria-invalid`; seção de empresa abre quando precisa de correção. Quebra de palavras no resumo evita overflow com erro de validação, texto a 200% e largura de 320 px, sem cortar conteúdo.
- A infraestrutura usa `MemberRegistrationValidationException` para falhas esperadas de negócio. A action deixa de tratar qualquer `ArgumentException` como simples formulário inválido. Exceções inesperadas continuam no diagnóstico centralizado, sem virarem sucesso.
- Proteções de CPF/e-mail único, índices, concorrência, confirmação, honeypot, antiforgery, vínculo empresarial pendente e separação administrativa foram preservadas. Não foi criado bypass de MFA ou ativação automática.

Comprovação: cinco novos cenários HTTP de entrada inválida; jornada existente com MySQL, persistência, confirmação por token privado e login; testes existentes de concorrência e recuperação na suíte completa. No navegador, CPF e confirmação inválidos aparecem juntos, o nome permanece, a senha é limpa e o foco vai ao resumo.

**Limite:** SMTP local continua desabilitado. O teste de confirmação usa o token da outbox protegida no host de integração; isso não comprova entrega numa caixa postal. A interface mantém a mensagem neutra e não afirma envio concluído.

## 2. Cuia: continuidade e limites

Causas encontradas:

1. O índice de referências era consultado somente com a pergunta atual. “E como posso confirmar o cardápio atual?” perdia a referência do Na Maré.
2. O histórico em memória podia ser substituído por um valor antigo inválido no `sessionStorage`. Esse defeito foi reproduzido no novo teste com armazenamento bloqueado.
3. A reserva da sessão serializava novamente o pedido completo, contando escapes do JSON externo e do JSON aninhado como conteúdo adicional. Isso superestimava a entrada e podia encerrar a sessão cedo. Não se comprovou que essa era a única causa das falhas relatadas.

Correções:

- Continuação curta sobre confirmar cardápio consulta as perguntas anteriores quando a pergunta atual não identifica outro lugar. Referências ambíguas pedem esclarecimento; uma nova identificação explícita não é substituída silenciosamente pelo lugar anterior.
- Resposta de confirmação orienta procurar o canal oficial e distingue referência editorial de cardápio atual. Não inventa contato, pratos ou funcionamento.
- Memória da aba compartilhada entre página e widget, com validação do formato, persistência opcional e contingência quando o navegador bloqueia armazenamento. Limpar a conversa remove essa memória, sem reiniciar a quota protegida.
- Normalização do contrato de resposta recusa corpo sem resposta e tolera coleções opcionais ausentes ou malformadas. Falha de JSON/renderização não deixa a interface travada.
- Controle de sequência e cancelamento na página principal evita que resposta antiga reapareça após limpar a conversa; estados de carregamento e botão são liberados em `finally`. O widget preserva esse controle e distingue timeout.
- Reserva calcula bytes UTF-8 dos textos decodificados enviados ao modelo, teto de saída e margem de 512 unidades. O payload interno evita escapes Unicode desnecessários. Continua sendo estimativa conservadora, não tokenização exata. Tetos de 24.000 por sessão, 800 de saída, 20 tentativas/dia e 200/mês não foram aumentados.
- Teste com o prompt real do serviço comprovou quatro perguntas curtas dentro do teto existente usando provedor simulado e reserva acumulada. Contextos maiores e outras tentativas continuam consumindo cota; não se promete número fixo de perguntas para toda sessão.

### Homologação real pequena

Duas requisições consecutivas, mesma sessão, sem interceptar o provedor:

| Pergunta | Resultado | Tempo |
| --- | --- | --- |
| Como conhecer a culinária paraense? | Gemini HTTP 200, `generationStatus=ok`, resposta gerada com 488 caracteres. | 4,58 s |
| E uma opção vegetariana em Belém? | Google HTTP 503, `generationStatus=unavailable`; portal HTTP 200 com orientação local identificada, 302 caracteres. | 1,83 s |

Primeira chamada: **616 tokens de entrada e 142 de saída**, custo local registrado zero. Segunda: serviço retornou erro sem medição de tokens; não foi inventado total nem descontada a reserva da tentativa. Contadores compartilhados passaram de 16 para 18 tentativas no dia e no mês. Não houve nova chamada para insistir no provedor, mudança de plano ou uso de fallback pago.

Evidências: `ai-live.json`, `ai-usage.json`, `usage-before.json`, `usage-after.json` e `web-final.log`. O segundo registro histórico de `ai-usage.json` é anterior à homologação e não deve ser contado como parte dessas duas chamadas.

Caso Na Maré: duas perguntas reais no portal usando a referência revisada já existente; a segunda menciona Na Maré, o canal oficial e a limitação da fonte. Nenhum restaurante foi publicado e nenhuma pesquisa aberta foi alegada. Captura: `ui/real-menu-continuation-390.png`.

## 3. Imagens e recursos públicos

- Favicon SVG próprio e ICO real adicionados; layouts públicos e administrativos apontam para o SVG. Arquivos são servidos anonimamente pelo middleware estático.
- Recursos públicos ausentes nos diretórios `/images`, `/css`, `/js` e os caminhos de favicon retornam 404 anônimo, sem redirecionamento administrativo. A fallback policy administrativa foi preservada.
- `image-resilience.js` atende imagens estáticas e inseridas dinamicamente. Em erro, remove `srcset` inválido, usa SVG local de apoio, altera o texto alternativo e apresenta aviso visível. A ilustração não é apresentada como foto do estabelecimento.
- Para imagem remota perto da área visível, espera máxima de 12 segundos. Se o próprio SVG falhar, esconde a imagem quebrada e mantém aviso/área de apoio; sem repetição em loop.
- Dimensões e atribuições existentes foram mantidas. Fotos documentais continuam com créditos e licenças da coleção anterior. Nenhuma foto, avaliação ou vídeo foi copiado nesta Sprint.
- Na inspeção visual foi corrigido também um caractere corrompido no ícone da Cuia; validação final em `widget-icon-final.png`.

Auditoria real: seis páginas (`/`, `/busca`, `/descobrir`, um detalhe publicado, maniçoba e receitas), **13 arquivos/candidatos locais únicos**, incluindo resoluções de `srcset`, todos HTTP 200 e decodificados no navegador. Não foram encontrados arquivos quebrados nessa amostra. Isso não prova disponibilidade de todas as imagens remotas ou de todo o catálogo.

Falhas simuladas e comprovadas: 404, resposta 200 com bytes inválidos, timeout remoto e 404 do próprio placeholder. Este último fez uma única requisição ao placeholder e preservou o aviso, sem loop. Evidências: `images.json`, `images.log`, `image-404.png`, `image-invalid.png`, `image-timeout.png`, `image-fallback-404.png`.

## 4. Testes e capturas

- **Build Release da solução:** zero erros e zero avisos (`build-final.log`).
- **Unitários:** 351 aprovados, zero falhas (`unit-final.log`).
- **Integração:** 202 aprovados, um ignorado, zero falhas (`integration-final.log`). O ignorado é preexistente e depende de credenciais administrativas persistentes; não foram alteradas senhas reais para executá-lo.
- **Interface:** 15 cenários aprovados (`ui/report.json`) com axe, overflow, erros JS e capturas. Formulário e fotos em 320/390/768/1440; fotos e formulário com erros a 200%; associações acessíveis dos erros; página/widget com quatro perguntas, erro → nova tentativa, 429, JSON inválido, corpo incompleto, falha de rede, timeout e armazenamento bloqueado; limpar conversa; referência real na continuação; imagem de apoio.
- **Plataformas:** 24 combinações Pixel 7/Chromium e iPhone 13/WebKit, retrato/paisagem, seis rotas. Sem overflow/erros JS; controles de senha e alvos de toque verificados (`platforms/report.json`). Emulação em Windows, não aparelhos físicos.
- **Imagens:** seis páginas, 13 candidatos decodificados e quatro estados de falha, além de favicon e 404 público.
- **Verificação final:** sintaxe de sete arquivos JavaScript, `git diff --check` e varredura de padrões de credenciais sem ocorrências nos diretórios verificados.

Capturas anteriores comparáveis estão em `artifacts/sprint48/ui`; as atuais em `artifacts/sprint49/ui`. Os estados de imagens quebradas foram induzidos para teste, não identificados como fotografias específicas reportadas pelo usuário.

Os testes de IA em massa usam simulação, não chamadas externas repetitivas. A suíte MySQL foi executada com o Worker local parado; ao concluir, o supervisor foi retomado e processo/heartbeat confirmados em `worker-final.json`. Não foi instalado serviço nem feita implantação.

## 5. Uso local e pendências

No Visual Studio, **pare a instância atual, recompile e execute novamente** para carregar os controllers e views corrigidos. O perfil mantém abertura em `https://127.0.0.1:63859/`, isolado dos cookies de `localhost`; instruções em [VISUAL-STUDIO-LOCAL.md](VISUAL-STUDIO-LOCAL.md). A validação desta Sprint usou o servidor local de revisão em `http://localhost:5180`, separado da instância Debug já aberta pelo usuário.

Nenhuma nova migração foi necessária. Configurações privadas e limites existentes foram preservados.

**AGUARDANDO CONFIGURAÇÃO OU SERVIÇO EXTERNO:**

1. SMTP e homologação de recebimento real para completar confirmação/recuperação por e-mail.
2. Disponibilidade consistente do Gemini: geração inicial foi comprovada, mas a segunda requisição real recebeu 503. A interface permanece utilizável com fallback identificado e nova tentativa manual dentro das quotas.
3. Conta/chave de busca aberta, se desejada; referência indexada não substitui busca atual na internet.
4. URLs específicas de outras fotos remotas que continuem indisponíveis para corrigir suas origens; a contingência visual já cobre os erros. Não se alteram fontes, licenças ou fotos de estabelecimentos sem evidência.
