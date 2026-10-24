# Belém Restaurantes — O Pará tem sabor

> **Sprint 76 — o portal ganha mapa para o mundo:** `/robots.txt` e `/sitemap.xml` existem de verdade — **16.424 URLs geradas só do que responde no portal público** (16.162 publicados, receitas, roteiros, cidades, localidades, categorias), reconciliada seção por seção contra SQL direto nos testes: o pendente não aparece, e rota estática quebrada quebra o build. Acheados no caminho: os `[ResponseCache]` (sitemap, robots, export ODbL) estavam inertes desde sempre — o middleware do .NET 10 nunca foi registrado (renomeado para `AddResponseCaching`), agora serve em ttfb ≤ 35 ms — e 4 municípios duplicados na tabela IBGE (mesmo nome, código divergente) foram deduplicados por URL no sitemap. Consulte [relatório e evidência ao vivo](docs/SPRINT76-REPORT.md).
>
> **Sprint 75 — a pesquisa do Worker ganha pools próprios:** a renovação de cotas da meia-noite era comida em ~15 min pela pesquisa contínua do Worker (152 jobs/dia, 132 marcados `quota_exhausted`), deixando a Cuia interativa sem cota o dia inteiro. Agora o Worker reserva em `worker_drael_search`/`worker_public_site_search` e os pools da Cuia ficam intocados por construção; probe ao vivo pela fiação de produção provou a separação. Bônus: um bug latente de hermeticidade dos testes (Gemini real vazando via user-secrets e fazendo o teste do Ask piscar à meia-noite) foi caçado e corrigido. Consulte [relatório e evidência ao vivo](docs/SPRINT75-REPORT.md).
>
> **Sprint 74 — a busca da Cuia volta a trazer fontes:** o "unavailable, 0 fontes" da Sprint 73 tinha dono — a busca nativa do drael.sh demora 12,9–17,2 s na vida real e o orçamento fixo de 15 s cortava o stream no meio; mediu-se, ajustou-se (orçamento configurável `Ai:Drael`, default 20 s), o `FailureCode` agora circula pelo composite e o log da Cuia mostra o motivo quando degrada. Resultado ao vivo: a mesma classe de pergunta responde em 12,7 s com **estado ok e 3 fontes reais clicáveis** (Quem, O Liberal, UOL) e repetição em 290 ms de cache. Consulte [relatório e evidência ao vivo](docs/SPRINT74-REPORT.md).
>
> **Sprint 73 — Cuia responde uma vez (e responde de pé) + Atlas Aberto:** a pergunta com pesquisa web deixou de rodar dois fan-outs completos — o caminho principal reutiliza o resultado da etapa culinária, provado por contador exato e pelo log "reutilizada da etapa culinária" — e a busca Drael reserva sua própria cota diária `drael_search` (teto de geração `cuia_drael` congelado durante as sondas ao vivo). Cliente que fecha a aba a meio caminho não pinta mais página vermelha (OCE tratado no controller; 408 na API), erros 4xx/5xx em navegação ganham página amigável em pt-BR, e o catálogo ganha exportação aberta em GeoJSON/CSV (`/dados-abertos`, ODbL, BOM UTF-8, cabeçalho estável) com contagens honestas: 286 de 16.162 publicações têm coordenada verificada, e a página diz. Consulte [relatório e evidência ao vivo](docs/SPRINT73-REPORT.md).
>
> **Sprint 72 — o atalho zero-HTTP prova o último elo:** a promessa da Sprint 67 — "todos os espelhos confirmados velhos → adiar sem reservar" — roda pela primeira vez num ciclo de geocodificação real (fila, teto diário e banco de verdade): com espelhos controlados, a ficha adia em `overpass_stale_data` com **zero requisições Overpass e zero unidades de cota**, e o controle mostra o contrafactual (só o espelho desconhecido é sondado, exatamente +2 unidades). Redundância de espelhos públicos avaliada na mesma janela e arquivada por evidência: nenhum dos quatro candidatos respondia a partir desta rede. Consulte [relatório e evidência ao vivo](docs/SPRINT72-REPORT.md).
>
> **Sprint 71 — espelhos no CLI e fonte única do veredito:** `--verify-geocoding` agora mostra, junto da fila, os espelhos Overpass com o mesmo veredito conservador do painel (regra única em um helper compartilhado pelas duas superfícies). Os testes arrancaram dois defeitos reais: uma `NullReferenceException` latente no painel para linha de idade ilegível (exatamente a linha osm.ch real) e um `switch` Razor que nunca havia aplicado cor aos badges. Consulte [relatório e evidência ao vivo](docs/SPRINT71-REPORT.md).
>
> **Sprint 70 — diagnóstico de falha provado com falha controlada:** a tag anotada `ci-log`, que jamais tinha saído do papel, foi exercitada de verdade no `main` — uma sonda temporariamente vermelha fez o CI publicar o teste falho, a mensagem exata e o rastro do log como tag antes de falhar o job, revelando e corrigindo o bug latente que impedia isso (o errexit padrão do Actions encerrava o script antes de capturar o código de saída). Na mesma janela, o frescor sticky dos espelhos (Sprint 67) foi corroborado em processo longevo: espelho confirmado velho foi pulado nas consultas seguintes dentro da janela. Consulte [relatório e evidência ao vivo](docs/SPRINT70-REPORT.md).
>
> **Sprint 69 — frescor dos espelhos no painel:** a idade dos dados de cada espelho Overpass deixa de viver só na memória do Worker — cada resposta real persiste exatamente o que o espelho respondeu (`osm3s.timestamp_osm_base`; ilegível vira `NULL`, nunca suposição), e o `/admin/geocodificacao` mostra espelho por espelho a data-base, quando foi vista e o veredito conservador (fresco / dado antigo / idade ilegível / sem observação) — com ela, dá para ver na tela por que fichas seguem em `overpass_stale_data`. Consulte [relatório e evidência ao vivo](docs/SPRINT69-REPORT.md).
>
> **Sprint 68 — o Worker aplica migrações de novo:** o "Database:ApplyMigrations=false" de todo boot era uma chave stale no cofre local de user-secrets (fora do repositório) da era em que o Worker não aplicava schema — a sobreposição silenciosa agora tem nome: `ConfigurationDiagnosticsService` avisa no boot quando qualquer fonte de maior precedência diverge dos appsettings, e o runner roda serializado por lock no banco. Consulte [relatório e evidência ao vivo](docs/SPRINT68-REPORT.md).
>
> **Sprint 67 — frescor sticky dos espelhos Overpass:** a idade dos dados de cada espelho passa a ser observada por processo (`osm3s.timestamp_osm_base` de toda resposta real); dentro da janela configurada (60 min) um espelho comprovado com dado mais velho que 14 dias não recebe requisição nem consome cadência, e quando todos estão confirmados velhos o ciclo adia a ficha **antes** de reservar cota — zero unidade do teto, zero HTTP. Consulte [relatório e evidência ao vivo](docs/SPRINT67-REPORT.md).
>
> **Sprint 66 — cache de consulta:** a resposta real do Nominatim para (endereço, cidade) servida hoje é rejogada dentro do mesmo dia UTC sem consumir o teto diário — chave SHA-256 da consulta exata, só respostas de fato bem-sucedidas (falhas/restrições nunca congelam); endereços duplicados (~3,3 mil fichas pendentes) e o segundo olhar só-do-logradouro custam zero cota após a primeira resposta. Consulte [relatório e evidência ao vivo](docs/SPRINT66-REPORT.md).
>
> **Sprint 65 — cache negativo de logradouro:** quando uma busca real de nível de logradouro confirma que um logradouro+cidade não tem candidato no OSM, a conclusão é persistida (`geocoding_street_cache`, janela de 7 dias) e as próximas fichas do mesmo escopo são encerradas como `no_candidates_street` gastando zero unidades do teto diário — sem o cache, cada ficha redescobria a mesma ausência por 1–2 unidades. Consulte [relatório e evidência ao vivo](docs/SPRINT65-REPORT.md).
>
> **Sprint 64 — segundo olhar para ruas sem candidato:** quando o endereço completo não devolve nada ao Nominatim (`no_candidates`), o Worker repete a busca só com o logradouro (mesma avaliação estrita, mais uma unidade do teto diário) antes de rejeitar; o esgotamento das duas buscas vira terminal `no_candidates_street`, e rua encontrada segue para a cadeia Overpass da Sprint 62. Consulte [relatório e evidência ao vivo](docs/SPRINT64-REPORT.md).

> **Sprint 63 — visibilidade e retomada por motivo de rejeição:** o painel `/admin/geocodificacao` e o `--verify-geocoding` distribuem as linhas terminais pelo motivo da rejeição (`SkipReasons`), e uma nova ação humana "Re-enfileirar" por motivo devolve à fila inteira uma categoria rejeitada quando a capacidade muda (ex.: o lote de `unsupported_type` que agora passa pelo fallback Overpass da Sprint 62) — com auditoria `geocoding.requeue_by_reason`. Consulte [relatório e validação](docs/SPRINT63-REPORT.md).

> **Sprint 62 — fallback Overpass:** quando o Nominatim só conhece o logradouro (sem objeto de endereço), o Worker consulta construções/restaurantes num raio de 80 m via Overpass API e aplica apenas com confirmação estrita (logradouro e número casando, objeto único, caixa do Pará); espelho com dados antigos só confirma, não rejeita, e cada consulta consome o mesmo teto diário compartilhado. Consulte [relatório, decisões e evidência ao vivo](docs/SPRINT62-REPORT.md).

> **Sprint 61 — ajuste humano de coordenada aplicada:** o painel `/admin/geocodificacao` mostra a coordenada real de cada ficha com link para conferir o ponto no OpenStreetMap, cartões rooftop/approximate, e botão "Ajustar" (par pré-preenchido, precisão declarada, caixa do Pará validada) com histórico `admin_review` e auditoria; revert agora também aceita origem já ajustada. Consulte [relatório e validação](docs/SPRINT61-REPORT.md).

> **Sprint 60 — revisão humana das coordenadas:** no painel `/admin/geocodificacao`, a linha completada ganha "Reverter coordenada": remove a rooftop/precisão aplicada pelo OSM (só origem Nominatim, Tier 1 intacto), devolve a ficha a rejeição revisada `reverted_by_admin` e grava histórico e auditoria; nova tentativa só por ação humana. CLI equivalente: `--revert-geocode <id>`. Consulte [relatório e validação](docs/SPRINT60-REPORT.md).

> **Sprint 59 — painel de geocodificação:** `/admin/geocodificacao` (moderador + MFA) mostra fila, contagens e motivos da geocodificação autônoma e permite devolver rejeições/falhas à fila, por ficha ou em lote (7+ dias), com auditoria. Consulte [relatório e testes](docs/SPRINT59-REPORT.md).

> **Sprint 58 — geocodificação autônoma:** o Worker localiza fichas publicadas sem coordenada pelo Nominatim/OpenStreetMap com regras rígidas (estado Pará, objeto de nível de edifício ou endereço confirmado, logradouro e número casando, ambiguidade rejeitada). Coordenada escrita uma vez, Tier 1 protegido, teto diário compartilhado no banco e rejeições honestas ficam em fila administrativa. Consulte [relatório, decisões de uso, evidência real e limites](docs/SPRINT58-REPORT.md).

> **Camada Guia e fotos com licença:** a home e a Cuia IA recomendam só a camada Guia. Publicar em lote cria ficha. Sem dono, horário, cardápio ou foto licenciada, o publicado volta a ficha. `/mapa` desenha só quem está publicado e tem coordenada. `/pratos` e `/ocasioes` separam recomendação de cadastro. O robô só grava foto do Wikimedia Commons com licença livre e o nome do lugar no arquivo; imagem do site da casa fica pendente. `/travessias` mostra barco apenas com fonte vigente. A leitura da ficha não inventa prato nem hora.

> **Sprint 25 — catálogo e leitura:** corrigida a consulta administrativa, com artes por categoria, controles para baixa visão, gestão de vídeos e lotes de 1.000 registros (teto configurável de 2.000). Consulte [funcionalidades, testes e limites da validação](docs/SPRINT25-PORTAL.md).

> **Aprovação mais prática:** navegação no topo e no fim, salto para página, seleção entre páginas, filtros preservados e consultas por IDs com índices novos. Consulte [mudanças, medições e testes da paginação](docs/SPRINT25-PAGINACAO.md).

> **Sprint 19 — receitas e operação estadual:** 12 receitas editoriais publicadas, CMS com versões, aprovação/publicação em etapas, dois estabelecimentos reais publicados após revisão, descoberta configurada para 144 municípios, rotinas de validade e novo visual de receitas e visitas. Consulte [relatório, testes, acesso local e pendências de produção](docs/SPRINT19-REPORT.md). A expansão rumo a 100 mil continua sujeita a fontes, deduplicação e moderação.

> **Expansão estadual e novo design:** coleta em 21 atividades econômicas, importação em lotes com checkpoint no banco, teto de 100 mil novos registros por comando e painel `/admin/cobertura`. Novo explorador das seis regiões, busca por município e tipo de comida em PT/EN/ES. A meta de 100 mil não representa cadastros já obtidos: consulte [quantidades verificadas, execução e limites](docs/EXPANSAO-PARA-100K.md).

Plataforma gastronômica e comunitária do Pará desenvolvida em ASP.NET Core 10 (`net10.0`), C#, MVC/Razor, Dapper e MySQL `belrestaurante`.  
**Empresa / Proprietário:** MNSOFT Soluções em Tecnologia (CNPJ: `18.160.057/0001-13`)  
**E-mail Oficial:** `comercial@belemrestaurantes.com.br`  
**Ambiente:** VPS Windows + IIS 10; Publicação manual pelo Visual Studio Community 2026; Descoberta autônoma contínua via Windows Service `BelemRestauranteCuiaDiscovery`.

> **Sprints 16 e 17 — operação e visitas:** central de aprovação em lote, monitoramento do Worker, três contas reais de homologação com MFA e troca inicial, gestão de acessos e conteúdo editorial, Cuia Rotas, horários com procedência e administração de fontes/tarifas. Consulte [funcionalidades, acesso local, testes e pendências externas](docs/SPRINT16-17-REPORT.md). O planejamento não inventa linhas, tarifas ou funcionamento; integrações externas ainda não homologadas estão identificadas no relatório.

> **Sprint 15 — validação de 18/10/2026:** adicionados **5.266 cadastros reais de empresas do setor de alimentação**, totalizando **5.747 em 135 municípios do Pará**, pendentes de moderação. Nova identidade editorial “O Pará. À flor do paladar.”, arte amazônica original, explorador de municípios, busca e entrada em PT/EN/ES. Consulte [relatório, evidências e limites da fonte](docs/SPRINT15-REPORT.md). Histórico: [Sprint 14](docs/SPRINT14-REPORT.md), [Sprint 13](docs/SPRINT13-REPORT.md) e [Sprint 12](docs/SPRINT12-REPORT.md).


---

## 🌟 Principais Módulos da Plataforma (Sprints 09, 10 e 11)

1. **Design System Belém Restaurantes V4 (`~/css/design-v4.css`):**
   - Identidade amazônica com paleta autêntica: Verde Floresta (`#1e7a54`), Dourado Tucupi (`#e5a93b`), Vermelho Urucum (`#d94326`) e Creme (`#fdfbf7`).
   - Cards gastronômicos de alto impacto, badges de status operacional (`operational`, `temporarily_closed`, `permanently_closed`), e conformidade WCAG 2.2 AA.

2. **Cobertura Territorial Oficial dos 144 Municípios do Pará (IBGE):**
   - Todos os 144 municípios oficiais do Pará cadastrados com códigos IBGE canônicos de 7 dígitos e mesorregiões.
   - Distinção clara entre municípios e localidades/ilhas fluviais (Icoaraci, Ilha do Combu, Mosqueiro, Outeiro, Cotijuba, Alter do Chão, Algodoal, Joanes).
   - URLs limpas e canônicas: `/cidades` e `/localidade/{slug}`.

3. **Cuia Discovery Intelligence & Crawler Autônomo:**
   - Fila central com 8 tipos de tarefas: Descoberta, Atualização, Enriquecimento, Verificação de Fontes, Deduplicação, Validação de Fechamento, Revisão de Mídias e Resolução de Conflitos.
   - Execução cíclica em lotes a cada 10 minutos com telemetria em tempo real persistida no MySQL (`discovery_run_telemetry`).
   - Proteção rigorosa contra **SSRF e DNS Rebinding** (bloqueio de IPs privados, loopback e metadados de nuvem 169.254.169.254).

4. **Qualidade do Catálogo Gastronômico (Profile Completeness Score):**
   - Avaliação algorítmica de completude do perfil (0 a 100%) priorizando estabelecimentos sem foto verificada, sem endereço ou sem contato.
   - Proteção estrita aos dados fornecidos por comerciantes verificados (Tier 1), direcionando divergências para moderação com auditoria.

5. **Motor de Mídias e Fotografias Autorizadas por Tiers:**
   - *Tier 1:* Fotos do Proprietário Verificado.
   - *Tier 2:* Fotos da Comunidade Aprovada.
   - *Tier 3:* Mídia Externa Licenciada (com atribuição e sem scraping invasivo).
   - *Tier 4:* Ilustração Oficial Padrão do Belém Restaurantes.

6. **Monetização Real e Faturas PIX MNSOFT:**
   - Chave PIX oficial CNPJ MNSOFT: `18.160.057/0001-13`.
   - Emissão de faturas de planos parceiros com TxID único, cópia-e-cola e autenticação antifraude via HMAC-SHA256 (`auth_hash`).

7. **Painel Central de Operações 360 (`/admin/discovery360`):**
   - Telemetria de execuções do worker (tempo de resposta, ciclos, registros descobertos e atualizados).
   - Gestão de faturas comerciais e confirmação autenticada.
   - Indicadores territoriais de cobertura do IBGE por mesorregião e cidade polo.
   - Moderação e auditoria contínua de alterações de atributos.

---

## 🚀 Desenvolvimento e Manutenção

1. **Requisitos:** .NET 10.0 SDK, Visual Studio Community 2026, MySQL 8.0+.
2. **Compilar em Release:**
   ```powershell
   dotnet build BelemRestaurantes.sln -c Release
   ```
3. **Executar Testes Automatizados:**
   ```powershell
   # Testes Unitários
   dotnet test tests/BelemRestaurantes.UnitTests -c Release

   # Testes de Integração de Localização
   dotnet test tests/BelemRestaurantes.IntegrationTests --filter "FullyQualifiedName~LocalizationTests" -c Release
   ```
4. **Executar Migrações do MySQL:**
   ```powershell
   dotnet run --project src/BelemRestaurantes.Web -- --migrate
   ```
5. **Provisionar Primeiro Administrador:**
   ```powershell
   dotnet run --project src/BelemRestaurantes.Web -- --provision-admin
   ```
6. **Publicação para IIS e Windows Service (VPS Windows):**
   ```powershell
   # Publicação do Portal Web para IIS
   dotnet publish src/BelemRestaurantes.Web/BelemRestaurantes.Web.csproj -c Release -o bin/Publish/WebRelease

   # Publicação do Worker permanente de Descoberta (Windows Service)
   dotnet publish src/BelemRestaurantes.Worker/BelemRestaurantes.Worker.csproj -c Release -o bin/Publish/WorkerRelease
   ```

---

## 📄 Relatórios Técnicos Oficiais

- [Relatório da Sprint 11 (Homologação Integral, Segurança Avançada e Qualidade)](docs/SPRINT11-REPORT.md)
- [Relatório da Sprint 10 (Operação Gastronômica 360 e Monetização Real)](docs/SPRINT10-REPORT.md)
- [Relatório da Sprint 09 (Cuia Discovery 360 e Cobertura Estadual)](docs/SPRINT09-REPORT.md)
- [Relatório da Sprint 08 (Cuia Crawler: Pesquisa Autônoma e Segurança SSRF)](docs/SPRINT08-REPORT.md)
- [Relatório da Sprint 06 (Redesign Completo e Jornada Gastronômica)](docs/SPRINT06-REPORT.md)
- [Relatório da Sprint 05 (Portal do Parceiro, Cardápio e Monetização)](docs/SPRINT05-REPORT.md)
- [Relatório da Sprint 04 (Comunidade, Fotos e Selo Cuia)](docs/SPRINT04-REPORT.md)
- [Relatório da Sprint 03 (Governança e Migração)](docs/SPRINT03-REPORT.md)
