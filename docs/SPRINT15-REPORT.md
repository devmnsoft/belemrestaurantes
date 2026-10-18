# Sprint 15 — expansão estadual e um guia do Pará para o mundo

Validação local em 18/10/2026, America/Sao_Paulo.

**Resultado confirmado no MySQL: 5.266 novos cadastros de empresas do setor de alimentação, passando de 481 para 5.747 registros, distribuídos em 135 municípios do Pará.** Todos permanecem `pending`. Publicação no portal depende da moderação; situação cadastral ativa não confirma atendimento presencial, funcionamento ou qualidade.

## Expansão com dados comprovados

A fonte complementar é o cadastro público de empresas da Receita Federal, acessado pelo [Minha Receita](https://docs.minhareceita.org/). A [API paginada documentada](https://docs.minhareceita.org/como-usar/) permite filtrar UF e atividade principal. A data de extração informada pelo endpoint `/updated` é **2026-10**; ela difere da data em que cada página foi coletada localmente.

A consulta foi limitada a `uf=PA` e aos CNAEs principais `5611201`, `5611203`, `1091102` e `4721102`: restaurantes, lanchonetes e atividades de padaria/confeitaria. Referência: [estrutura da CNAE publicada pelo IBGE/CONCLA](https://concla.ibge.gov.br/images/concla/documentacao/EstruturaDetalhadaCNAE_CNAEFiscal2_Atualizada.pdf). Pizzaria, açaí, cafeteria, hamburgueria e tacacá são subcategorias inferidas por termos explícitos no nome comercial, sujeitas à revisão.

O parser exige UF PA, situação cadastral ativa, CNPJ numérico com dígitos verificadores válidos, nome fantasia utilizável e município do catálogo oficial. Se código IBGE e nome do município divergem, o registro é descartado. Não usa razão social como substituta de nome fantasia e não materializa quadro societário, e-mail ou campos de CPF. Coordenadas, site, nota e fotos não são inventados.

| Evidência | Resultado |
| --- | ---: |
| Catálogo antes desta evolução | 481 |
| Entrada congelada para a carga principal | 52 páginas / 53.248 registros da fonte |
| Registros elegíveis após o parser nessa entrada | 5.306 |
| Registros filtrados antes da ingestão | 47.942 |
| Criações confirmadas na primeira amostra, ciclo 921 | 1.098 |
| Criações confirmadas na carga principal, ciclo 925 | 4.168 |
| Atualizações/reencontros na carga principal | 1.138 |
| **Acréscimo líquido no catálogo** | **5.266** |
| **Catálogo final** | **5.747** |
| Municípios com cadastros | **135** |
| Falhas de ingestão nas duas cargas | **0** |
| Restaurantes de fixtures restantes | **0** |

A primeira amostra integra a entrada da carga principal; seus registros foram reencontrados, sem somar duas vezes ao total. Mais de um identificador da fonte pode ser associado ao mesmo cadastro por deduplicação de nome/endereço. Contagens da fonte, escritas e linhas distintas do catálogo não são equivalentes. A confirmação de que não há duplicatas físicas exige a revisão dos casos suspeitos.

A carga principal começou às **08:39:29 UTC** e terminou às **08:40:39 UTC**. Uma reimportação de uma página já processada, ciclo 926, resultou em **zero novos, 104 atualizações e zero falhas**, preservando os 5.747 registros. Essa conferência reutilizou o arquivo local e não fez novas consultas externas.

## Composição atual do catálogo

| Tipo | Total |
| --- | ---: |
| Restaurante | 2.589 |
| Lanchonete | 1.187 |
| Padaria | 1.177 |
| Pizzaria | 260 |
| Cafeteria | 154 |
| Açaí | 145 |
| Hamburgueria | 79 |
| Bar | 70 |
| Feira e mercado | 23 |
| Confeitaria | 20 |
| Sorveteria | 19 |
| Tacacá | 13 |
| Hortifruti | 4 |
| Praça de alimentação | 4 |
| Peixaria | 2 |
| Empório | 1 |
| **Total** | **5.747** |

Esses totais abrangem o catálogo anterior e a expansão atual, sempre pendentes de moderação. A carga fiscal não foi usada para fingir novas feiras, avaliações, fotos ou coordenadas.

## Operação da coleta e da importação

- Coletor `scripts/fetch-food-registry.py`: páginas de até 1.024 registros, projeção mínima antes de salvar, manifesto de coleta, cursor opaco preservado, retomada e espera para HTTP 429/`Retry-After`. Não troca IPs ou servidores para contornar limites.
- A coleta foi encerrada após a meta ter sido confirmada no banco. O cache pode conter páginas adicionais às 52 da entrada congelada; elas não são contabilizadas como cadastros importados. Não se afirma esgotamento da base estadual nem cobertura de todos os 144 municípios.
- Comando `--import-food-registry`: arquivo local limitado por tamanho/página, mesmo orquestrador e regras de ingestão do Worker, teto de novas inserções, deduplicação, observação de origem, telemetria por município e auditoria com SHA-256 do arquivo.
- O provedor local `minha_receita` não aciona classificação de IA paga. IDs `cnpj:` não são enviados ao Google Details.
- Importações pontuais usam a telemetria manual prevista na migration 020. A fila agendada é preservada. O Worker normal permanece separado da Web, em Development, com Discovery/Crawler em ciclos de um minuto; a fonte OSM mantém cache de 24 horas.
- Diagnóstico `--verify-discovery` agrega cargas manuais por ciclo/fonte e contabiliza municípios. As quantidades vêm de consultas e transações reais.

Os dados da Receita são disponibilizados por uma fonte secundária e podem conter desatualização, incompletude ou inconsistência, conforme a [explicação do próprio Minha Receita](https://docs.minhareceita.org/sobre-os-dados/). O portal explica essa distinção em `/guia`. A atribuição OpenStreetMap/ODbL e sua exportação continuam separadas do cadastro empresarial.

## Identidade visual e funcionalidades

- Nova direção editorial: **“O Pará. À flor do paladar.”**, com entrada em português, inglês e espanhol. Verde de floresta, roxo de açaí, tucupi, palha, urucum e azul de rio; tipografia editorial, mais espaço e hierarquia clara.
- Nova capa original de açaí, peixe, tacacá, rios e mercado. Identificada como ilustração editorial criada com IA. Versão WebP de **457.054 bytes**, cerca de 86,2% menor que o PNG; não representa fotografia de estabelecimento.
- Home reconstruída com pesquisa nos 144 municípios oficiais, atalhos por categoria, manifesto, seis portas de entrada por destino, catálogo publicado, comunidade, favoritos, explicação de fontes/revisão e Portal do Parceiro. Selo Cuia e experiências continuam condicionados aos dados reais existentes.
- Explorador `/cidades` reconstruído com busca por nome e filtro por região do catálogo territorial, funcionando por GET inclusive sem JavaScript. Mantém distinção entre municípios e ilhas/distritos. Contagens exibidas incluem apenas publicados; pendências não viram números promocionais.
- Nova página `/guia` explica fontes, revisão, cuidados antes da visita e como enviar correções.
- Busca, navegação, categorias principais, cards e mensagens de autocomplete/geolocalização receberam textos nos três idiomas principais. Seletor rápido no cabeçalho mantém CSRF e retorno local.
- Os oito idiomas previamente suportados continuam disponíveis. Para os cinco idiomas além de PT/EN/ES, os novos textos editoriais usam **inglês com aviso explícito**; não se declara tradução completa de todos os módulos legados.
- Nomes de cidades e estabelecimentos permanecem os da fonte. A arte, as regiões e os textos não foram usados para fabricar estabelecimentos, menus, notas, horários ou visitas verificadas.

[Prompt integral, método e arquivos da arte](design/SPRINT15-IMAGE-PROMPT.md).

## Qualidade dos dados e próximos passos operacionais

Consulta final de qualidade:

- **5.747 pendentes** e zero fixtures no catálogo.
- **5.266 sem coordenadas**: os novos cadastros empresariais não foram geocodificados. O filtro de proximidade considera somente lugares com coordenadas.
- **1.680 sem endereço** e **1.860 sem telefone**, campos preservados como ausentes.
- A revisão precisa confirmar atividade atual, endereço, contatos, possíveis duplicatas e direitos das fotos antes de publicar. Os dados cadastrais não certificam que um local recebe visitantes.

Chave válida de Google Places continua necessária apenas se esse provedor for escolhido. Rotação de credenciais expostas historicamente, tradução integral dos módulos legados e implantação na VPS continuam fora da execução local desta entrega.

## Verificação

- Build Release: **zero erros e avisos**.
- **200 testes unitários aprovados**: filtros de atividade/UF/status/município, validação de CNPJ, minimização de dados, categoria, duplicação de IDs, ausência de coordenadas inventadas e módulos anteriores.
- **59 testes de integração selecionados aprovados**: histórico das 20 migrations, segurança, jornadas públicas e administrativas, ingestão/moderação/publicação com fixtures, contagens territoriais excluindo pendentes e páginas editoriais/filtragem de destinos nos três idiomas principais.
- **110 casos responsivos aprovados**, cobrindo páginas públicas, snapshots autenticados e exemplos controlados nas larguras 360, 390, 768, 1024 e 1440 px, sem overflow, erros JavaScript ou violações nas regras axe selecionadas.
- A auditoria encontrou e corrigiu o nome acessível do seletor PT/EN/ES. Os roteiros visuais foram espaçados e serializados para respeitar os limites reais da aplicação.
- **40 casos adicionais em inglês/espanhol aprovados**, sem overflow, erros JavaScript ou violações nas regras axe selecionadas: home, busca, cidades e guia nas mesmas cinco larguras. Inclui interação com busca de município e filtro regional. Com os 110 casos principais, são **150 casos responsivos aprovados**. Resultados em `artifacts/sprint15/locale-summary.json`.
- Axe executa regras WCAG A/AA selecionadas em 360 px. Isso não equivale a certificação integral de acessibilidade ou pentest.
- Testes que consomem a fila operacional foram excluídos. Fixtures são removidas e não entram no total descoberto.
- Artefatos locais de publicação: `artifacts/sprint15/publish/Web` e `artifacts/sprint15/publish/Worker`. Nenhuma implantação externa foi executada.

## Evidências e reprodução

Evidências locais, ignoradas pelo Git:

- `artifacts/sprint15/baseline-evidence.json` — catálogo anterior, 481 registros.
- `artifacts/sprint15/food-pages-import.ndjson` — entrada congelada e minimizada das 52 páginas, com horários de coleta.
- `artifacts/sprint15/receita/updated.json`, `manifest.json`, `checkpoint.json` e logs de coleta — data da base, cursores, progresso e esperas por limitação.
- `artifacts/sprint15/import-early.log`, `import-state.log` e `import-repeat.log` — as cargas e a verificação de reimportação.
- `artifacts/sprint15/discovery-evidence.json` e `catalog-quality.json` — consultas ao banco com totais, fontes, municípios e qualidade.
- `artifacts/sprint15/catalogo-para-revisao.csv` — os 5.747 cadastros para revisão local, com origem e identificação, sem campos de sócios/e-mail. Texto potencialmente interpretado como fórmula é escapado na exportação.
- `artifacts/sprint15/unit.trx`, `integration.trx`, logs de build/testes, `visual/report.json`, `locales/report.json`, resumos e PNGs.

```powershell
# Coleta pontual com retomada; não faz parte de requisições dos visitantes.
python scripts/fetch-food-registry.py --output artifacts/food-registry --target 5500 --delay 15

# Usar uma cópia congelada do arquivo. Reprocessamento reutiliza identificadores.
dotnet run --project src/BelemRestaurantes.Worker -c Release --no-build --no-launch-profile -- --environment Development --import-food-registry --file=artifacts/food-registry/food-pages-import.ndjson --max-new=5000

# Consulta somente leitura; não imprime credenciais.
dotnet run --project src/BelemRestaurantes.Worker -c Release --no-build --no-launch-profile -- --environment Development --verify-discovery
```

A conexão permanece nos User Secrets/ambiente. O arquivo congelado deve ser preparado após a coleta; não importar um arquivo ainda em gravação. `--max-new` limita novas inserções nessa execução, e não o tamanho final do catálogo.

## Arquivos desta evolução

Nenhuma migration nova. Mantidos os 20 scripts existentes, sem alteração do conteúdo aplicado. Arquivos envolvidos nesta etapa, além do README e deste relatório:

- `scripts/fetch-food-registry.py`, `scripts/verify-atlas-locales.cjs`, `scripts/verify-responsive.cjs`.
- `src/BelemRestaurantes.Infrastructure/OpenData/FoodRegistryParser.cs`.
- `src/BelemRestaurantes.Worker/FoodRegistryImport.cs`, `Program.cs`, `DiscoveryEvidence.cs`.
- `src/BelemRestaurantes.Application/Services/DiscoveryOrchestrator.cs`.
- `src/BelemRestaurantes.Infrastructure/Google/GooglePlacesClient.cs`.
- `src/BelemRestaurantes.Application/Abstractions/IRepositories.cs`.
- `src/BelemRestaurantes.Infrastructure/MySql/MySqlRestaurantReadRepository.cs`.
- `src/BelemRestaurantes.Infrastructure/Search/MySqlSearchService.cs`.
- `src/BelemRestaurantes.Web/Controllers/HomeController.cs`, `SearchController.cs`.
- `src/BelemRestaurantes.Web/Models/CatalogCategories.cs`, `ParaDestinations.cs`.
- `src/BelemRestaurantes.Web/Views/Home/Index.cshtml`, `Guide.cshtml`.
- `src/BelemRestaurantes.Web/Views/Search/Index.cshtml`, `Cities.cshtml`.
- `src/BelemRestaurantes.Web/Views/Shared/_Layout.cshtml`, `_CategoryShortcuts.cshtml`, `_RestaurantCard.cshtml`.
- `src/BelemRestaurantes.Web/Resources/SharedResource*.resx`.
- `src/BelemRestaurantes.Web/wwwroot/css/para.css`, `para-atlas.css`.
- `src/BelemRestaurantes.Web/wwwroot/js/para.js`, `catalog.js`.
- `src/BelemRestaurantes.Web/wwwroot/images/para-atlas-editorial-v1.png`, `para-atlas-editorial-v1.webp`.
- `tests/BelemRestaurantes.UnitTests/FoodRegistryTests.cs`, `OpenDataDiscoveryTests.cs`.
- `tests/BelemRestaurantes.IntegrationTests/LocalizationTests.cs`, `CatalogJourneyTests.cs`.
- `docs/design/SPRINT15-IMAGE-PROMPT.md`.
