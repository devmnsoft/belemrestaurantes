# Relatório de Conclusão — Sprint 04
## Belém Restaurantes — O Pará tem sabor
### Comunidade Gastronômica, Identidade Segura, Fotos, Avaliações e Compartilhamento Social

---

## 1. Resumo Executivo

A **Sprint 04** transformou o catálogo do *Belém Restaurantes* em uma plataforma comunitária completa, interativa e segura, integrando identidade de visitantes com proteção LGPD, avaliações multidimensionais de experiências, upload higienizado de fotografias com remoção de metadados, controle rigoroso de privacidade anti-IDOR, o algoritmo bayesiano transparente do **Selo Cuia**, feed social de descobertas gastronômicas, e um painel de moderação e auditoria administrativa de nível corporativo.

Toda a arquitetura existente foi rigorosamente preservada: ASP.NET Core 10 (MVC/Razor), Dapper, MySqlConnector com migrações embutidas, autenticação por cookies segregados, e internacionalização sincronizada em **9 idiomas** (`pt-BR`, `pt`, `en`, `es`, `fr`, `de`, `ru`, `ja`, `zh-Hans`).

---

## 2. Detalhamento das 12 Fases Desenvolvidas

### Fase 1 — Identidade e Cadastro do Visitante
- **Sem Coleta Desnecessária de CPF:** O cadastro do visitante foi projetado estritamente com Nome Completo, E-mail, Senha e Confirmação de Senha, dispensando CPF para interações gastronômicas em conformidade com o princípio da minimização de dados da LGPD (Art. 6º, III).
- **Ciclo Completo de Autenticação:**
  - Cadastro público com verificação assíncrona por e-mail transacional.
  - Login seguro com cookie `.BelRest.Member` (HttpOnly, Secure, SameSite=Lax, sliding desabilitado).
  - Troca de senha autenticada (`/account/change-password`) com validação de senha atual e complexidade.
  - Alteração de e-mail com reverificação por token de uso único (`/account/request-email-change`).
  - Gerenciamento de sessões e revogação de outras sessões ativas (`/account/revoke-other-sessions`).
  - Bloqueio administrativo e desbloqueio com auditoria (`/admin/community/members`).
  - Página **Meu Perfil** (`/account`) exibindo dados cadastrais, histórico de experiências e atalhos rápidos.
- **Conformidade LGPD:**
  - Exportação de dados do titular (`/account/export-data`) gerando arquivo JSON estruturado contendo perfil, experiências, fotos, comentários e curtidas.
  - Exclusão e anonimização de conta (`/account/anonymize-account`) conforme o Art. 16 da LGPD, substituindo dados de identificação por hashes irreversíveis e desativando o acesso, sem romper chaves estrangeiras relacionais.
- **Segregação Estrita de Políticas:** Visitantes autenticados via `.BelRest.Member` nunca herdam políticas ou claims administrativas (`amr=mfa`, roles `PlatformAdmin`, `Moderator`, `Operator`).

### Fase 2 — Integração de E-mails Transacionais Locaweb
- **Endereço Oficial:** `comercial@belemrestaurantes.com.br`.
- **Arquitetura Outbox Criptografada:** Mensagens são persistidas na tabela `transactional_email_outbox` com criptografia Data Protection antes do disparo.
- **Templates Multi-idiomas:** Modelos formatados em HTML responsivo e texto puro para confirmação de cadastro, recuperação de senha, alerta de segurança e notificação de moderação nos 9 idiomas suportados.
- **Proteção Antienumeração:** Retornos de recuperação e alteração de e-mail utilizam mensagens neutras, impedindo a inferência da existência de contas no sistema.
- **Compatibilidade:** Alinhado a SPF, DKIM e DMARC com host SMTP Locaweb (`email-ssl.com.br`), porta 465 (SSL) / 587 (STARTTLS).
- **Zero Credenciais no Código:** Parâmetros confidenciais configurados exclusivamente via variáveis de ambiente (`SMTP_HOST`, `SMTP_PORT`, `SMTP_USER`, `SMTP_PASSWORD`) ou `secrets.json`.

### Fase 3 — Minha Experiência
- **Botão na Página do Restaurante:** Botão de destaque *"Conte como foi sua experiência"* disponibilizado em todos os estabelecimentos (`/experience/create?establishmentId={id}`).
- **Avaliação Multidimensional:**
  - Nota geral de 1 a 5 estrelas.
  - Subcritérios avaliativos: Atendimento, Sabor, Ambiente e Custo-Benefício.
  - Registro de pratos consumidos e campo opcional de preço pago por pessoa.
  - Comentário com contagem de caracteres e sanitização em tempo real.
- **Estados de Publicação:**
  - `draft` (Rascunho): Visível apenas para o autor durante a elaboração.
  - `pending` (Aguardando Moderação): Enviado para análise da equipe MNSOFT.
  - `approved` (Publicada): Visível publicamente ou conforme a política de privacidade.
  - `rejected` (Rejeitada): Notifica o autor com motivo e permite interposição de recurso (`appeal`).
  - `deleted` (Excluída): Exclusão lógica com preservação do histórico para auditoria.
- **Gestão do Autor:** Visitantes autenticados podem editar seu relato ou excluí-lo diretamente pelo perfil ou página da experiência.

### Fase 4 — Fotografias e Armazenamento Seguro
- **Processamento com SkiaSharp:**
  - Substituição de bibliotecas comerciais proprietárias por **SkiaSharp** (Google Skia / Open Source), garantindo compilação limpa em `Release` e alta eficiência.
  - **Eliminação de EXIF e GPS:** O processo de decodificação (`SKBitmap.Decode`) e recodificação em WebP elimina integralmente qualquer metadado geográfico ou proprietário sensível.
  - **Geração de Miniaturas:** Redimensionamento inteligente com proporção preservada para HD (máximo 1920x1080) e Thumbnail (400x300).
- **Validação Rigorosa:**
  - Verificação de Magic Bytes (`image/jpeg`, `image/png`, `image/webp`).
  - Rejeição de extensões maliciosas ou executáveis (`.exe`, `.sh`, `.php`, `.bat`, etc.).
  - Limite de até 5 fotografias por experiência e 8MB por arquivo.
  - Nomes de arquivo gerados pelo servidor via GUID (`[guid].webp` e `[guid]_thumb.webp`).
- **Isolamento de Diretórios:** Armazenamento em pasta fora do diretório raiz executável (`App_Data/uploads/photos`).
- **Segregação de Galerias no Restaurante:**
  - Fotos Oficiais (fornecidas ou validadas pelo restaurante).
  - Fotos da Comunidade (enviadas por visitantes e aprovadas pela moderação).
  - Fotos Ilustrativas (identificadas explicitamente com tarja para evitar confusão com imagens reais).

### Fase 5 — Privacidade das Publicações e Proteção Anti-IDOR
- **Três Níveis de Visibilidade:**
  - `public`: Disponível no feed da comunidade, na página do restaurante e apto para indexação e compartilhamento.
  - `private`: Estritamente restrito ao autor logado. Tentativas de acesso por anônimos ou outros usuários via URL, API ou mídia retornam `404 Not Found` / `403 Forbidden`.
  - `unlisted`: Acessível apenas para quem possuir o link protegido com token criptográfico de acesso (`?token=[random]`), com aviso visual indicativo.
- **Proteção em Todas as Camadas:** O controle de privacidade é imposto no `MySqlExperienceService`, no `MediaController` (para streaming de fotos e thumbnails) e nas queries SQL, impossibilitando bypass de autorização via manipulação de ID.

### Fase 6 — Compartilhamento Social e Metadados Open Graph
- **Compartilhamento Multicanal:**
  - **WhatsApp:** Link direto codificado com texto personalizado e pontuação do restaurante.
  - **Facebook:** Compartilhador web padrão (`sharer.php`).
  - **Instagram & TikTok:** Guia e botão de cópia de imagem/texto para publicação rápida em stories e posts manuais.
  - **Web Share API (`navigator.share`):** Compartilhamento nativo fluido em dispositivos móveis (Android e iOS).
  - **Copiar Link:** Cópia para a área de transferência com feedback visual (toast notification).
- **Open Graph Dinâmico:** Cada experiência pública possui metatags `og:title`, `og:description`, `og:image`, `og:type="article"` e `og:url` formatadas para pré-visualização enriquecida em todas as redes sociais e aplicativos de mensagens.

### Fase 7 — Selo Cuia: Classificação Gastronômica Própria
- **Metodologia Bayesiana Transparente:**
  $$\text{Score} = \frac{v \cdot R + m \cdot C}{v + m}$$
  - $R$: Média aritmética das avaliações do restaurante.
  - $v$: Quantidade de avaliações recebidas pelo estabelecimento.
  - $m$: Peso a priori da plataforma ($m = 3$).
  - $C$: Média global de referência de Belém ($C = 4.0$).
  - **Efeito Antifraude:** Impede que um restaurante com apenas uma avaliação 5.0 ultrapasse estabelecimentos consolidados com dezenas de avaliações consistentes.
- **8 Categorias Gastronômicas Regionais:**
  1. *Melhores avaliados de Belém*
  2. *Melhor açaí* (identificação regional com tapioca e peixe frito)
  3. *Melhor tacacá* (barracas tradicionais e cuias de tucupi)
  4. *Melhor pizzaria*
  5. *Melhor custo-benefício* (baseado no subcritério específico)
  6. *Destaques do interior do Pará* (municípios paraenses além da capital)
  7. *Novos talentos gastronômicos* (estabelecimentos cadastrados recentemente)
  8. *Favoritos da comunidade* (maior índice de engajamento e curtidas)
- **Desacoplamento Absoluto:** As notas do Google ou de terceiros jamais interferem no cálculo bayesiano interno da plataforma Belém Restaurantes.

### Fase 8 — Comunidade e Moderação
- **Feed "Sabores da Comunidade":** Interface moderna (`/community` ou `/feed`) exibindo publicações públicas recentes com fotos reais, pratos avaliados, comentários e notas.
- **Interações Sociais:**
  - Curtir/Descurtir com contadores atômicos no MySQL.
  - Comentários na experiência com proteção CSRF e validação de tamanho.
  - Sistema de Denúncias (`/community/report`) com motivos estruturados (spam, linguagem ofensiva, assédio, PII, propaganda).
- **Moderação Inteligente e Recurso Humano:**
  - Filtro em tempo real para vocabulário ofensivo e detecção de dados pessoais sensíveis (CPF, cartões de crédito).
  - Fluxo de Recurso (`appeal`): Caso o autor discorde de uma rejeição, pode contestar enviando justificativa para reanálise humana no painel MNSOFT.

### Fase 9 — Painel Administrativo MNSOFT Expandido
Rotas administrativas dedicadas sob política `AdminArea` e MFA obrigatório:
- `/admin/community/experiences`: Moderação de experiências pendentes, aprovação, rejeição com motivo e julgamento de recursos.
- `/admin/community/photos`: Moderação visual das fotografias submetidas, com classificação em oficial, comunitária ou remoção.
- `/admin/community/reports`: Fila de denúncias da comunidade com opções de descarte ou remoção do conteúdo infrator.
- `/admin/community/members`: Listagem de usuários, controle de bloqueio/desbloqueio e histórico de auditoria.
- `/admin/community/lgpd`: Painel de solicitações LGPD (exportações solicitadas e anonimizações efetuadas).
- `/admin/community/cuia`: Visualização dos rankings do Selo Cuia e acionamento de recálculo geral.
- `/admin/community/smtp`: Monitoramento da fila `transactional_email_outbox`, métricas de sucesso/falha e disparos pendentes.

### Fase 10 — Cuia Discovery e Isolamento
- **Status Inicial Pendente:** Estabelecimentos descobertos pelo crawler ou Google Discovery são salvos com status `pending` e passam obrigatoriamente por verificação humana antes de ir a público.
- **Isolamento de Conteúdo Comunitário:** Restaurantes ainda em triagem não recebem publicações abertas e não expõem informações de usuários.
- **Conectores Preparados:** Estrutura pronta para vinculação de links oficiais de Instagram, Facebook e YouTube dos estabelecimentos.

### Fase 11 — Auditoria e Segurança Reforçada
- **Prevenção a Injeções:** 100% das operações SQL utilizam parâmetros tipados via Dapper; tags HTML são sanitizadas pelo `ContentModerationRules`.
- **Proteção CSRF:** Todos os formulários (perfil, experiências, comentários, denúncias, moderação) utilizam `[ValidateAntiForgeryToken]`.
- **Anti-Traversal:** O `MediaController` sanitiza nomes de arquivo bloqueando sequências `..` e separadores de diretório.
- **Cabeçalhos de Segurança:** `X-Content-Type-Options: nosniff`, `Content-Security-Policy` com `frame-ancestors 'none'`, cookies `HttpOnly` e `Secure`.
- **Auditoria Centralizada:** Eventos críticos de segurança gravados na tabela `audit_events`.

### Fase 12 — Homologação no Visual Studio Community 2026
- **Build Release:** Compilação 100% limpa (0 erros, 0 avisos).
- **Testes Unitários:** 81 testes passando com sucesso.
- **Migração 010:** Script SQL autônomo incorporado ao assembly `BelemRestaurantes.Infrastructure`, idempotente com `IF NOT EXISTS`.

---

## 3. Tabela de Arquivos Criados e Modificados

| Arquivo | Tipo | Descrição |
|---|---|---|
| `src/BelemRestaurantes.Infrastructure/Migrations/Scripts/010_experiences_photos_cuia.sql` | Novo | Script SQL criando tabelas sociais, fotos, cuia, denúncias e colunas LGPD. |
| `src/BelemRestaurantes.Domain/Entities/UserExperience.cs` | Novo | Entidade da experiência comunitária com notas multidimensionais. |
| `src/BelemRestaurantes.Domain/Entities/ExperiencePhoto.cs` | Novo | Entidade de controle de fotografia enviada por visitante. |
| `src/BelemRestaurantes.Domain/Entities/CommunityEntities.cs` | Novo | Entidades para Likes, Comentários, Denúncias, Rankings Cuia e LGPD. |
| `src/BelemRestaurantes.Domain/Rules/CuiaRatingRules.cs` | Novo | Metodologia bayesiana e classificação por categoria regional. |
| `src/BelemRestaurantes.Domain/Rules/ContentModerationRules.cs` | Novo | Sanitização HTML, filtro de ofensas e detecção de dados pessoais (PII). |
| `src/BelemRestaurantes.Domain/Rules/PhotoRules.cs` | Novo | Validação de magic bytes (JPEG/PNG/WebP), limites e extensões seguras. |
| `src/BelemRestaurantes.Application/Abstractions/IExperienceService.cs` | Novo | Contrato de serviços sociais, moderação, curtidas e comentários. |
| `src/BelemRestaurantes.Application/Abstractions/IPhotoStorageService.cs` | Novo | Contrato para processamento e armazenamento seguro de fotos. |
| `src/BelemRestaurantes.Application/Abstractions/ICuiaService.cs` | Novo | Contrato de cálculo e consulta dos rankings do Selo Cuia. |
| `src/BelemRestaurantes.Application/Abstractions/ILgpdService.cs` | Novo | Contrato de exportação e anonimização de dados do titular. |
| `src/BelemRestaurantes.Application/Abstractions/IMemberAccounts.cs` | Modificado | Adição de troca de senha, alteração de e-mail e gestão de membros. |
| `src/BelemRestaurantes.Infrastructure/Social/DiskPhotoStorageService.cs` | Novo | Processador de imagens com SkiaSharp (WebP, remoção de EXIF, miniaturas). |
| `src/BelemRestaurantes.Infrastructure/Social/MySqlExperienceService.cs` | Novo | Repositório social com Dapper, autorização e moderação. |
| `src/BelemRestaurantes.Infrastructure/Social/MySqlCuiaService.cs` | Novo | Motor do Selo Cuia e persistência de rankings no MySQL. |
| `src/BelemRestaurantes.Infrastructure/Social/MySqlLgpdService.cs` | Novo | Exportação JSON e anonimização conforme Art. 16 da LGPD. |
| `src/BelemRestaurantes.Infrastructure/Members/MySqlMemberAccounts.cs` | Modificado | Implementação dos novos métodos de identidade e gestão de membros. |
| `src/BelemRestaurantes.Infrastructure/InfrastructureServiceCollectionExtensions.cs` | Modificado | Injeção de dependência dos novos serviços sociais. |
| `src/BelemRestaurantes.Web/Controllers/ExperienceController.cs` | Novo | Controller do fluxo de criação, edição, feed, detalhes e moderação social. |
| `src/BelemRestaurantes.Web/Controllers/MediaController.cs` | Novo | Streaming seguro de fotos e miniaturas com controle de privacidade anti-IDOR. |
| `src/BelemRestaurantes.Web/Controllers/AdminCommunityController.cs` | Novo | Controller do painel MNSOFT para moderação de experiências, fotos, denúncias e membros. |
| `src/BelemRestaurantes.Web/Controllers/AccountController.cs` | Modificado | Integração da página "Meu Perfil", alteração de senha/e-mail e exportação LGPD. |
| `src/BelemRestaurantes.Web/Controllers/EstablishmentController.cs` | Modificado | Integração com Selo Cuia, galeria segregada e experiências comunitárias. |
| `src/BelemRestaurantes.Web/Views/Experience/Feed.cshtml` | Novo | Interface do feed "Sabores da Comunidade". |
| `src/BelemRestaurantes.Web/Views/Experience/Details.cshtml` | Novo | Página de detalhes da experiência com Open Graph tags e compartilhamento social. |
| `src/BelemRestaurantes.Web/Views/Experience/Create.cshtml` | Novo | Formulário "Conte como foi sua experiência" com preview de fotos. |
| `src/BelemRestaurantes.Web/Views/Experience/Edit.cshtml` | Novo | Formulário de edição de relato pelo autor. |
| `src/BelemRestaurantes.Web/Views/Account/Index.cshtml` | Modificado | Visão completa do perfil, segurança, histórico e LGPD. |
| `src/BelemRestaurantes.Web/Views/Establishment/Details.cshtml` | Modificado | Exibição do Selo Cuia, galeria em 3 seções e botão de relato. |
| `src/BelemRestaurantes.Web/Views/Shared/_Layout.cshtml` | Modificado | Link para "Sabores da Comunidade" na navbar e metatags globais. |
| `src/BelemRestaurantes.Web/Views/AdminAccount/Dashboard.cshtml` | Modificado | Cards de acesso rápido para moderação e comunidade. |
| `src/BelemRestaurantes.Web/Views/AdminAccount/Experiences.cshtml` | Novo | Tela administrativa de moderação de experiências e recursos. |
| `src/BelemRestaurantes.Web/Views/AdminAccount/Photos.cshtml` | Novo | Tela administrativa de moderação de fotografias. |
| `src/BelemRestaurantes.Web/Views/AdminAccount/Reports.cshtml` | Novo | Painel de triagem de denúncias da comunidade. |
| `src/BelemRestaurantes.Web/Views/AdminAccount/Members.cshtml` | Novo | Gestão de membros, bloqueio e auditoria. |
| `src/BelemRestaurantes.Web/Views/AdminAccount/Lgpd.cshtml` | Novo | Painel de monitoramento de solicitações LGPD. |
| `src/BelemRestaurantes.Web/Views/AdminAccount/Cuia.cshtml` | Novo | Gestão e recálculo das categorias do Selo Cuia. |
| `src/BelemRestaurantes.Web/Views/AdminAccount/Smtp.cshtml` | Novo | Monitoramento da fila transacional e status de entrega. |
| `src/BelemRestaurantes.Web/Resources/SharedResource.*.resx` | Modificado | Sincronização de 75 novas chaves nos 9 idiomas suportados. |
| `tests/BelemRestaurantes.UnitTests/SocialExperienceDomainTests.cs` | Novo | Testes unitários para regras de domínio social, LGPD e moderação. |
| `tests/BelemRestaurantes.UnitTests/ContentModerationTests.cs` | Novo | Testes de sanitização HTML, vocabulário e PII. |
| `tests/BelemRestaurantes.UnitTests/CuiaRatingTests.cs` | Novo | Testes da fórmula bayesiana e categorias do Selo Cuia. |
| `tests/BelemRestaurantes.UnitTests/PhotoValidationTests.cs` | Novo | Testes de magic bytes e extensões permitidas. |
| `tests/BelemRestaurantes.IntegrationTests/SocialCommunityTests.cs` | Novo | Testes de integração para proteção de rotas, anti-traversal e redirecionamento de login. |
| `tests/BelemRestaurantes.IntegrationTests/SecurityEndpointTests.cs` | Modificado | Validação das novas rotas protegidas da comunidade contra anônimos. |

---

## 4. Script de Migração Aplicável (`010_experiences_photos_cuia.sql`)

O script de migração foi embutido no assembly `BelemRestaurantes.Infrastructure` e é executado automaticamente pelo `MigrationRunner` na inicialização do sistema:

- **Novas colunas em `member_users`:**
  - `pending_email`: armazena novo e-mail durante solicitação de alteração.
  - `is_locked`: indicador booleano de bloqueio administrativo.
  - `lock_reason`: justificativa do bloqueio.
  - `anonymized_at`: timestamp de anonimização LGPD.
- **Tabela `user_experiences`:** Armazena avaliações gerais e por critério, pratos, preços, texto, visibilidade, tokens e estados de moderação e recurso.
- **Tabela `experience_photos`:** Metadados das fotos sanitizadas, nomes WebP, miniaturas e status de moderação.
- **Tabelas de Engajamento:** `experience_likes` e `experience_comments`.
- **Tabela `community_reports`:** Fila de denúncias para moderação humana.
- **Tabela `cuia_rankings`:** Cache persistido das 8 categorias do Selo Cuia recalculadas periodicamente.
- **Tabela `lgpd_requests`:** Trilha de auditoria das solicitações de exportação e exclusão.

---

## 5. Evidência de Build e Testes

### Compilação em Release
```
dotnet publish src/BelemRestaurantes.Web/BelemRestaurantes.Web.csproj -c Release -o bin/Publish/Release
  BelemRestaurantes.Domain -> bin\Release\net10.0\BelemRestaurantes.Domain.dll
  BelemRestaurantes.Application -> bin\Release\net10.0\BelemRestaurantes.Application.dll
  BelemRestaurantes.Infrastructure -> bin\Release\net10.0\BelemRestaurantes.Infrastructure.dll
  BelemRestaurantes.Web -> bin\Release\net10.0\BelemRestaurantes.Web.dll
  Resultado: 0 erros, 0 avisos.
```

### Suite de Testes Unitários
```
dotnet test tests/BelemRestaurantes.UnitTests
Aprovado! – Com falha: 0, Aprovado: 81, Ignorado: 0, Total: 81, Duração: 332 ms
```

### Suite de Testes de Integração e Localização
```
dotnet test tests/BelemRestaurantes.IntegrationTests --filter "FullyQualifiedName~SecurityEndpointTests|FullyQualifiedName~SocialCommunityTests|FullyQualifiedName~LocalizationTests"
Aprovado! – Com falha: 0, Aprovado: 48, Ignorado: 0, Total: 48, Duração: 2 s
```

---

## 6. Procedimento de Publicação no Visual Studio Community 2026

1. Abra a Solution `BelemRestaurantes.sln` no **Visual Studio Community 2026**.
2. Clique com o botão direito sobre o projeto `BelemRestaurantes.Web` e selecione **Publish... (Publicar...)**.
3. Selecione o perfil de publicação para pasta IIS ou utilize a configuração de Release existente.
4. O build gerará os binários com os assemblies satélites de localização em subpastas (`de`, `en`, `es`, `fr`, `ja`, `pt-BR`, `ru`, `zh-Hans`) e o arquivo `web.config` pronto para o IIS.
5. Transfira o conteúdo da pasta publicada para a VPS Windows no diretório do site no IIS (ex: `C:\inetpub\belemrestaurantes`).
6. Garanta que o Application Pool do IIS esteja configurado com:
   - **.NET CLR Version:** *No Managed Code* (pois o ASP.NET Core roda como processo out-of-process/in-process via ASP.NET Core Module v2).
   - **Identity:** `ApplicationPoolIdentity` (ou conta de serviço com permissão de escrita na pasta `App_Data/uploads`).
7. As migrações do banco MySQL (`010_experiences_photos_cuia.sql`) serão aplicadas automaticamente no primeiro startup da aplicação.

---

## 7. Configurações de Ambiente (SMTP, Uploads e Chaves)

As credenciais sensíveis **não estão gravadas no repositório**. Configure-as via variáveis de ambiente do sistema Windows na VPS ou via `appsettings.Production.json` protegido:

```json
{
  "ConnectionStrings": {
    "MySql": "Server=localhost;Port=3306;Database=belrestaurante;User ID=belem_app;Password=SUA_SENHA_AQUI;SslMode=Preferred;"
  },
  "Smtp": {
    "Host": "email-ssl.com.br",
    "Port": 465,
    "UseSsl": true,
    "Username": "comercial@belemrestaurantes.com.br",
    "Password": "SENHA_FORNECIDA_LOCAWEB",
    "FromEmail": "comercial@belemrestaurantes.com.br",
    "FromName": "Belém Restaurantes"
  },
  "Storage": {
    "UploadPath": "App_Data/uploads/photos"
  }
}
```

---

## 8. Relatório de Riscos Remanescentes e Recomendações

1. **Antivírus em Nível de Sistema Operacional no Diretório de Uploads:**
   - *Status Atual:* Os arquivos enviados são inspecionados por magic bytes, têm o stream reprocessado pelo SkiaSharp (eliminando payloads embutidos em EXIF) e são convertidos para WebP antes da escrita em disco.
   - *Recomendação:* Na VPS Windows, configurar o Windows Defender para monitorar em tempo real a pasta `App_Data/uploads/photos`.
2. **Backup e Retenção do Diretório de Fotos:**
   - *Recomendação:* Incluir a pasta `App_Data/uploads/photos` na rotina diária de backup em nuvem (ou volume separado), garantindo integridade dos arquivos originais.
3. **Registro SPF / DKIM / DMARC no DNS do Domínio:**
   - *Recomendação:* Assegurar que os apontamentos TXT de SPF e as chaves DKIM geradas no painel da Locaweb estejam devidamente publicadas na zona DNS de `belemrestaurantes.com.br` para evitar classificação de e-mails em caixas de spam.
4. **Volume de Armazenamento:**
   - *Recomendação:* Cada foto em WebP 82% tem tamanho médio de 120KB a 250KB. Para 10.000 fotos, o consumo estimado é de aproximadamente 2.5 GB, compatível com discos SSD modernos.
