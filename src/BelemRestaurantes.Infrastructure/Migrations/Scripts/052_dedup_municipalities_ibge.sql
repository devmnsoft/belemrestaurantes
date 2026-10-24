-- name: deduplicacao de municipios por codigo IBGE oficial e atribuicao correta de localidades
-- Requer MySQL 8.0+
-- Sprint 78. A tabela geo_municipalities carregava 4 linhas duplicadas com codigos IBGE que nao
-- existem no registro oficial do Para (reconciliado em 24/10/2026 com a API publica do IBGE:
-- https://servicodados.ibge.gov.br/api/v1/localidades/estados/15/municipios — 144 municipios):
--   1506304 (Salvaterra)              -> codigo oficial 1506302
--   1506353 (Santa Izabel do Para)    -> codigo oficial 1506500
--   1506544 (Santa Barbara do Para)   -> codigo oficial 1506351
--   1506585 (Santa Maria do Para)     -> codigo oficial 1506609
-- As duplicatas vieram do seed historico da migracao 013 (editada apos aplicada, entao o ajuste
-- vive nesta nova migracao — regra de checksum da casa). Adicionalmente, 013/014 amarraram as
-- zonas turisticas de Salinopolis (codigo oficial 1506203) ao codigo 1506500, que pertence a
-- Santa Izabel do Para — corrigido abaixo antes de apagar os codigos invalidos.
-- Idempotente: repetir nao afeta nenhuma linha na segunda execucao.

-- 1. Distritos/zonas de Salvaterra referenciando o codigo invalido 1506304 -> 1506302 (oficial)
UPDATE geo_districts_localities SET municipality_ibge = 1506302 WHERE municipality_ibge = 1506304;

-- 2. Zonas turisticas de Salinopolis referenciando 1506500 (Santa Izabel do Para) -> 1506203 (Salinopolis oficial)
UPDATE geo_districts_localities SET municipality_ibge = 1506203 WHERE municipality_ibge = 1506500 AND normalized_name IN ('praia do atalaia', 'praia do farol velho');

-- 3. Exclusao dos municipios duplicados (codigos ausentes do registro oficial IBGE)
DELETE FROM geo_municipalities WHERE ibge_code IN (1506304, 1506353, 1506544, 1506585);
