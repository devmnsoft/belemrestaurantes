# Sprint 78 — o mapa do estado tem exatamente 144 cidades: deduplicação dos municípios contra o registro oficial IBGE

Execução local em 24/10/2026. Nenhum segredo adicionado ou versionado.

## Origem

A Sprint 76 registrou como pendência os **4 pares de municípios duplicados** com código IBGE divergente. Em vez de consertar par por palpite, a regra da casa ("nunca inventar") mandava reconciliar a tabela inteira contra a fonte oficial: a API pública do IBGE (`/api/v1/localidades/estados/15/municipios`, consultada hoje, 144 municípios). A reconciliação mostrou que a divergência era **exatamente** essa — e revelou um defeito que ninguém tinha notado: as **duas zonas turísticas de Salinópolis** (Praia do Atalaia e Praia do Farol Velho, semeadas pela migração 014) estavam amarradas ao código `1506500`, que no registro oficial pertence a **Santa Izabel do Pará** — Salinópolis é `1506203`. No painel de inteligência de descoberta, as praias de Salinópolis apareciam como se fossem de outro município.

A raiz das duplicatas é histórica: o seed da migração 013 usou códigos que não existem no registro IBGE (`1506304`, `1506353`, `1506544`, `1506585`). Como script já aplicado não se edita — a regra de checksum do runner trata divergência como incidente (mensagem literal: "Crie uma nova migração em vez de editar esta") — o ajuste vive na **migração 052**.

## O que mudou

- **Migração `052_dedup_municipalities_ibge.sql`** (3 statements, idempotente): remapeia referências de localidades `1506304 → 1506302` (distritos de Salvaterra); devolve as duas zonas de Salinópolis para `1506203` usando filtro por nome normalizado (colateral zero sobre qualquer outra eventual localidade de Santa Izabel); apaga as 4 linhas com códigos ausentes do registro. Updates antes do delete para o FK com `ON DELETE CASCADE` nunca carregar dados.
- **`sql/INSTALACAO_LIMPA_COMPLETA_MARIADB_10_11.sql`**: ganhou o bloco 052 (statements + linha `schema_migrations` com checksum idêntico ao recurso embutido) antes da sanidade final; o comentário de sanidade estava obsoleto dizendo "15 migrations" — agora 52. Instalação limpa converge para o mesmo estado: o dump carrega o seed histórico antigo (que é o que as bases já migradas carregam) e o 052 faz a mesma reconciliação.
- **Guarda unitário** `CuiaExperienceV5Tests.Migrations_AreSequentialAndNonEmpty`: contagem 51 → 52 — esse teste existe exatamente para forçar quem adiciona migração a passar por ele com consciência.

## Achados da investigação (com números)

1. **Reconciliação integral antes/depois:** antes, 148 linhas (as 4 duplicatas) e 4 referências de localidades nos códigos errados; depois, **144 linhas = exatamente o registro IBGE, zero divergências de código ou nome e zero ausentes**. A validação foi código por código e nome por nome contra a API, não só sobre as quatro suspeitas.
2. **Por que todas as instalações tinham a mesma forma (148):** a migração 015 faz upsert dos 144 oficiais com `ON DUPLICATE KEY UPDATE`, então caminho limpo e caminhos já migrados convergem para o mesmo estado — o 052 basta uma vez, em qualquer base.
3. **Nenhuma URL pública muda:** o sitemap da S76 já publicava um slug por cidade (`DISTINCT` sobre a mesma transformação dos links internos), então `/cidade/{slug}` continua com 144 URLs. O que se corrige é integridade interna: o dropdown de municípios do `/admin/worker` listava cada cidade duplicada duas vezes; o agrupamento da inteligência de descoberta por `municipality_ibge` dividia uma cidade em dois códigos; e as zonas de Salinópolis saíam de sob Santa Izabel do Pará.

## Testes

- **Integração** — `Sprint78MunicipalityDedupTests` (2 fatos, aplicam as migrações reais no banco de teste): a tabela reflete exatamente o registro oficial (total 144, nenhum código inválido sobrevivendo, nenhum nome duplicado, e cada uma das quatro cidades resolve para **exatamente um** código — o oficial, verificado contra o conjunto `{1506302, 1506351, 1506500, 1506609}`); e as referências de localidades apontam para municípios existentes e corretos (zero referências penduradas via JOIN, distritos de Salvaterra no código oficial, zonas de Salinópolis de volta a Salinópolis).
- **Unidade** — guarda de sequenciamento de migrações atualizado 51 → 52; total estável em 546.

## Validação

- Build Release `--no-incremental` (worker parado): **0 avisos / 0 erros**.
- Unidade: **546/546**.
- Integração local: **254 aprovados / 1 ignorado (preexistente) / 0 falhas** — em duas execuções completas consecutivas, incluindo a reconciliação seção por seção do sitemap da S76 sobre a tabela deduplicada (contagem de cidades segue 144, como previsto).
- Ao vivo (detalhe em [`artifacts/sprint78-live-evidence.txt`](../artifacts/sprint78-live-evidence.txt)): snapshots ANTES/DEPOIS no banco real, linha do `schema_migrations` com checksum `6F4A78C2…D9BB0`, reaplicação idempotente, e reconciliação integral contra o IBGE zerada nos dois sentidos.

## Operação

Worker parado durante os rebuilds e reiniciado com o binário desta Sprint (supervisor 29356 / worker 28376, estado `running`, processo único verificado via CIM). A migração 052 foi aplicada no banco dev pela suíte de integração (17:42 UTC); o boot do worker a encontra já registrada. Evidência gitignoreada entra com `git add -f`. Nenhuma senha, MFA ou envio de e-mail alterado.

## Encerramento operacional

O mapa do estado agora tem prova: cada linha da tabela corresponde a um — e apenas um — município do registro oficial, com o nome e o código exatos, e cada localidade aponta para o município certo. Quatro cidades de dois nomes voltaram a ser uma só, e as praias de Salinópolis deixaram de morar em Santa Izabel. O resto do portal (sitemap, vitrine de cidades, inteligência de descoberta) herda a correção sem uma linha sua alterada.
