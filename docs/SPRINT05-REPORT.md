# Relatório de Conclusão — Sprint 05
## Belém Restaurantes — O Pará tem sabor
### Portal do Restaurante Parceiro, Comunidade Gastronômica, Cuia IA e Monetização

---

## 1. Missão e Resumo Executivo

A **Sprint 05** consolidou a visão estratégica do **Belém Restaurantes** como uma plataforma gastronômica completa, sustentável e segura, integrando perfeitamente seus **três motores**:

1. **Cuia Discovery:** Busca e monitoramento de estabelecimentos com fontes externas autorizadas, mitigando conflitos e atualizando dados a cada 10 minutos com o auxílio da **Cuia IA** (adaptadores Gemini, Groq e DeepSeek com salvaguardas).
2. **Comunidade Gastronômica:** Experiências reais de visitantes, subcritérios de notas, fotografias sanitizadas com SkiaSharp sem EXIF/GPS, cálculo bayesiano transparente do **Selo Cuia** e respeito à privacidade anti-IDOR e LGPD.
3. **Portal do Restaurante Parceiro:** Ambiente corporativo para proprietários reivindicarem seus restaurantes com comprovação de vínculo, gerenciarem dados comerciais, horários normais e especiais/feriados, publicarem cardápios digitais inteligentes com alérgenos e pratos regionais, responderem a clientes, acompanharem métricas de engajamento e contratarem planos comerciais com pagamento via **PIX MNSOFT (CNPJ: 18.160.057/0001-13)**.

Toda a arquitetura existente em ASP.NET Core 10, Dapper, MySQL `belrestaurante`, SkiaSharp e internacionalização em 9 idiomas foi preservada.

---

## 2. Auditoria Prévia: O que Já Existia vs. O que Foi Implementado

| Componente | Estado Anterior (Sprint 04) | Evolução na Sprint 05 |
|---|---|---|
| **Identidade de Visitantes** | Cadastro sem CPF, confirmação de e-mail e Meu Perfil. | Preservado e integrado aos fluxos de reivindicação de estabelecimentos comerciais. |
| **E-mails Transacionais** | SMTP Locaweb outbox criptografada (`comercial@belemrestaurantes.com.br`). | Preservado para envio de notificações de aprovação de reivindicação e pedidos. |
| **Comunidade & Moderação** | Feed público, likes, comentários, denúncias e Selo Cuia bayesiano. | Integrado com respostas oficiais dos restaurantes parceiros às avaliações dos clientes. |
| **Portal do Comerciante** | Inexistente. | **Criado integralmente**: Reivindicações, gestão de dados comerciais, comodidades, horários regulares e feriados. |
| **Cardápio Digital** | Inexistente. | **Criado integralmente**: Categorias, itens, alérgenos, pratos regionais paraenses, geração de QR Code de mesa e integração com WhatsApp. |
| **Cuia IA** | Classificação de dados e deduplicação no Worker. | **Expandido**: Assistente de descrição apetitosa de pratos regionais no cardápio, mantendo regras estritas contra alucinação de ingredientes ou preços. |
| **Monetização & Planos** | Inexistente. | **Criado integralmente**: 4 Planos (`Gratuito`, `Cuia Pro`, `Cuia Destaque`, `Cuia Redes`), ordens de compra e chave PIX MNSOFT. |
| **Mobilidade & Roteiros** | Link básico para Google Maps. | **Criado integralmente**: Módulo "Como Chegar e o que fazer por perto" e Roteiros Gastronômicos curados pelo Pará. |
| **Segurança Anti-IDOR** | Aplicado a experiências privadas. | **Estendido ao parceiro**: Nenhuma alteração é permitida a estabelecimentos que o usuário não comprove propriedade. |

---

## 3. Detalhamento dos Módulos Entregues

### 3.1 Portal do Restaurante Parceiro (`/partner`)
- **Fluxo de Reivindicação:** Comerciante solicita gestão do restaurante em `/partner/claim/{id}`, informando Razão Social, CNPJ/documento, telefones e evidências comprobatórias de vínculo.
- **Moderação Administrativa MNSOFT:** Painel dedicado em `/admin/partner/claims` para aprovação ou rejeição com justificativa pela equipe MNSOFT.
- **Painel do Comerciante (`/partner/dashboard`):** Lista exclusiva de restaurantes vinculados ao titular com autorização estrita anti-IDOR (`IPartnerAuthorizationService`).
- **Gestão Comercial:** Edição de slogan, descrição oficial, telefones, delivery no WhatsApp, links do iFood e delivery próprio, além de comodidades (Wi-Fi, Estacionamento, Ar Livre, Pet Friendly, Acessibilidade PCD).
- **Horários Regulares & Feriados:** Grade semanal completa e calendário de horários especiais (Círio de Nazaré, Natal, Ano Novo, folgas). O motor em tempo real `PartnerRules.IsOpenNow` calcula se o estabelecimento está aberto agora ou fechado considerando feriados.
- **Eventos e Novidades:** Divulgação de festivais gastronômicos, atrações e promoções em `/partner/{id}/announcements`.
- **Respostas às Avaliações:** Comerciantes respondem publicamente e de forma oficial aos relatos de experiências deixados pelos clientes.
- **Estatísticas de Visitas:** Métricas dos últimos 30 dias com visualizações de perfil, cliques para WhatsApp, rotas e acessos ao cardápio.

### 3.2 Cardápio Digital Inteligente com Cuia IA (`/cardapio/{id}`)
- Organização por categorias dinâmicas com ordenação personalizada.
- Itens com preço, ingredientes voluntariamente declarados, avisos sobre alérgenos e status de disponibilidade.
- **Reconhecimento de Culinária Paraense:** Identificação automática de pratos típicos (tacacá, maniçoba, pato no tucupi, açaí, vatapá, caruru, peixes amazônicos [filhote, pirarucu, pescada] e sobremesas de cupuaçu/bacuri).
- **Assistente Cuia IA:** Botão *"Sugerir com Cuia IA"* no painel do parceiro gera descrições apetitivas e culturalmente autênticas, sem inventar ingredientes ou preços.
- **Página Pública do Cardápio:** Interface fluida para smartphones com modal de **QR Code da Mesa** imprimível e botão direto para pedidos no WhatsApp.

### 3.3 Monetização e Apoio PIX MNSOFT
- Tabela de Planos Comerciais:
  - **Plano Gratuito:** Perfil básico, localização, horários e avaliações orgânicas.
  - **Plano Cuia Pro (R$ 79,90/mês):** Cardápio digital ilimitado, métricas de visitas e gestão completa.
  - **Plano Cuia Destaque (R$ 149,90/mês):** Todos os recursos Pro com exposição patrocinada identificada.
  - **Plano Cuia Redes (R$ 299,90/mês):** Gestão unificada de franquias e múltiplas filiais.
- Checkout com chave **PIX oficial MNSOFT (CNPJ: 18.160.057/0001-13)** e código PIX Copia e Cola gerado na tela de confirmação (`/partner/{id}/plans/checkout`).

### 3.4 Mobilidade e Turismo Gastronômico (`/roteiros`)
- Módulo "Como chegar e o que fazer por perto" integrado aos dados estruturados de transporte metropolitano e rodoviário.
- Roteiros curados:
  - *Circuito Belém Histórico & Ver-o-Peso*
  - *Rota Fluvial & Chocolate da Ilha do Combu*
  - *Orla de Icoaraci & Tradição da Cerâmica*
  - *Rota dos Sabores Atlânticos de Salinas*
  - *Sabor Tapajônico & Piracaia de Alter do Chão*

---

## 4. Tabela de Arquivos Criados e Modificados

| Arquivo | Ação | Finalidade |
|---|---|---|
| `src/BelemRestaurantes.Infrastructure/Migrations/Scripts/011_partner_portal_menu_monetization_ai.sql` | Novo | Tabelas de reivindicação, proprietários, perfil, cardápio, horários, monetização, eventos e roteiros. |
| `src/BelemRestaurantes.Domain/Entities/PartnerEntities.cs` | Novo | Entidades do parceiro, cardápio, pedidos PIX, estatísticas e roteiros. |
| `src/BelemRestaurantes.Domain/Rules/PartnerRules.cs` | Novo | Regras de pratos típicos paraenses e cálculo de status "Aberto agora / Fechado" com feriados. |
| `src/BelemRestaurantes.Application/Abstractions/IPartnerServices.cs` | Novo | Contratos de autorização anti-IDOR, portal do parceiro, cardápio, planos e mobilidade. |
| `src/BelemRestaurantes.Infrastructure/Partner/MySqlPartnerServices.cs` | Novo | Repositórios Dapper para reivindicações, perfis comerciais, horários e respostas oficiais. |
| `src/BelemRestaurantes.Infrastructure/Partner/MySqlMenuAndMonetizationServices.cs` | Novo | Repositórios Dapper para Cardápio com Cuia IA, Planos PIX MNSOFT e Roteiros Turísticos. |
| `src/BelemRestaurantes.Infrastructure/InfrastructureServiceCollectionExtensions.cs` | Modificado | Injeção de dependência dos serviços da Sprint 05. |
| `src/BelemRestaurantes.Web/Controllers/PartnerController.cs` | Novo | Controller do Portal do Parceiro com verificação anti-IDOR em todas as rotas. |
| `src/BelemRestaurantes.Web/Controllers/MenuController.cs` | Novo | Controller do Cardápio Digital Público e Gestão pelo Parceiro com Cuia IA. |
| `src/BelemRestaurantes.Web/Controllers/TrailsController.cs` | Novo | Controller de Roteiros Gastronômicos e Mobilidade do Pará. |
| `src/BelemRestaurantes.Web/Controllers/AdminPartnerController.cs` | Novo | Controller administrativo MNSOFT para moderação de reivindicações. |
| `src/BelemRestaurantes.Web/Views/Partner/Dashboard.cshtml` | Novo | Painel com estabelecimentos vinculados ao comerciante. |
| `src/BelemRestaurantes.Web/Views/Partner/Claim.cshtml` | Novo | Formulário de solicitação de reivindicação com comprovação de vínculo. |
| `src/BelemRestaurantes.Web/Views/Partner/Manage.cshtml` | Novo | Gestão de dados comerciais, delivery, comodidades e redes sociais. |
| `src/BelemRestaurantes.Web/Views/Partner/Schedule.cshtml` | Novo | Grade semanal regular e calendário de feriados/datas especiais. |
| `src/BelemRestaurantes.Web/Views/Partner/Announcements.cshtml` | Novo | Publicação e exclusão de eventos e novidades da casa. |
| `src/BelemRestaurantes.Web/Views/Partner/Stats.cshtml` | Novo | Painel de métricas de visitas, WhatsApp, rotas e cardápio. |
| `src/BelemRestaurantes.Web/Views/Partner/Plans.cshtml` | Novo | Grade comparativa dos 4 planos comerciais. |
| `src/BelemRestaurantes.Web/Views/Partner/OrderConfirmation.cshtml` | Novo | Tela de pagamento PIX com chave CNPJ MNSOFT e código Copia e Cola. |
| `src/BelemRestaurantes.Web/Views/Menu/ManageMenu.cshtml` | Novo | Gestão do Cardápio pelo parceiro com botão do assistente Cuia IA. |
| `src/BelemRestaurantes.Web/Views/Menu/PublicMenu.cshtml` | Novo | Cardápio digital público responsivo com QR Code e pedidos no WhatsApp. |
| `src/BelemRestaurantes.Web/Views/Trails/Index.cshtml` | Novo | Listagem dos roteiros turísticos e gastronômicos paraenses. |
| `src/BelemRestaurantes.Web/Views/Trails/Details.cshtml` | Novo | Roteiro com paradas, pratos destacados e dicas de transporte. |
| `src/BelemRestaurantes.Web/Views/AdminPartner/Claims.cshtml` | Novo | Tela administrativa para moderação e aprovação de reivindicações. |
| `src/BelemRestaurantes.Web/Views/Establishment/Details.cshtml` | Modificado | Adicionados botões de Ver Cardápio Digital e Reivindicar Restaurante. |
| `src/BelemRestaurantes.Web/Views/Shared/_Layout.cshtml` | Modificado | Adicionados links para Roteiros e Portal do Parceiro no cabeçalho. |
| `src/BelemRestaurantes.Web/Views/AdminAccount/Dashboard.cshtml` | Modificado | Adicionado card para Reivindicações de Comerciantes no painel MNSOFT. |
| `tests/BelemRestaurantes.UnitTests/PartnerRulesTests.cs` | Novo | Testes de pratos típicos paraenses e horários de funcionamento com feriados. |
| `tests/BelemRestaurantes.IntegrationTests/PartnerEndpointTests.cs` | Novo | Testes de proteção de rotas de parceiro e rotas públicas de roteiros. |

---

## 5. Evidência Real de Compilação e Testes

### Compilação em Release
```text
dotnet publish src/BelemRestaurantes.Web/BelemRestaurantes.Web.csproj -c Release -o bin/Publish/Release
  BelemRestaurantes.Domain -> bin\Release\net10.0\BelemRestaurantes.Domain.dll
  BelemRestaurantes.Application -> bin\Release\net10.0\BelemRestaurantes.Application.dll
  BelemRestaurantes.Infrastructure -> bin\Release\net10.0\BelemRestaurantes.Infrastructure.dll
  BelemRestaurantes.Web -> bin\Release\net10.0\BelemRestaurantes.Web.dll
  Resultado: 0 erros, 0 avisos.
```

### Testes Unitários
```text
dotnet test tests/BelemRestaurantes.UnitTests
Aprovado! – Com falha: 0, Aprovado: 91, Ignorado: 0, Total: 91, Duração: 665 ms
```

### Testes de Integração e Localização
```text
dotnet test tests/BelemRestaurantes.IntegrationTests --filter "FullyQualifiedName~PartnerEndpointTests|FullyQualifiedName~SecurityEndpointTests|FullyQualifiedName~SocialCommunityTests|FullyQualifiedName~LocalizationTests"
Aprovado! – Com falha: 0, Aprovado: 57, Ignorado: 0, Total: 57, Duração: 7 s
```

---

## 6. Procedimento de Publicação no Visual Studio Community 2026 para IIS

1. Abra a solution `BelemRestaurantes.sln` no **Visual Studio Community 2026**.
2. Clique com o botão direito no projeto `BelemRestaurantes.Web` e clique em **Publish (Publicar)**.
3. Escolha a pasta de publicação Release IIS.
4. Transfira os binários para a VPS Windows no diretório do site no IIS (`C:\inetpub\belemrestaurantes`).
5. Garanta que o Application Pool no IIS esteja configurado com **No Managed Code** e permissão na pasta `App_Data/uploads`.
6. Ao iniciar o site, o `MigrationRunner` aplicará o script `011_partner_portal_menu_monetization_ai.sql` de forma automática e segura no MySQL.

---

## 7. Configurações de Ambiente e Serviços

```json
{
  "ConnectionStrings": {
    "MySql": "Server=localhost;Port=3306;Database=belrestaurante;User ID=belem_app;Password=SENHA_PRODUCAO;SslMode=Preferred;"
  },
  "Ai": {
    "ProviderChain": ["Gemini", "Groq", "DeepSeek"]
  },
  "Smtp": {
    "Host": "email-ssl.com.br",
    "Port": 465,
    "UseSsl": true,
    "Username": "comercial@belemrestaurantes.com.br",
    "Password": "SENHA_FORNECIDA_LOCAWEB",
    "FromEmail": "comercial@belemrestaurantes.com.br",
    "FromName": "Belém Restaurantes"
  }
}
```

---

## 8. Relatório de Riscos Remanescentes

1. **Aprovação de Reivindicações:** Recomenda-se que a equipe MNSOFT contate o restaurante parceiro via WhatsApp ou Instagram oficial antes de aprovar a reivindicação para prevenir engenharia social.
2. **Confirmação de Pagamentos PIX:** Atualmente as ordens geram código Copia e Cola para a chave CNPJ oficial MNSOFT (`18.160.057/0001-13`) e são confirmadas pelo administrador ou webhook autenticado. A integração direta com API de emissão de cobrança PIX imediata (Pix Cob) poderá ser ativada assim que o banco fornecer as credenciais mTLS.
