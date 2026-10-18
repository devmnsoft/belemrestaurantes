-- Preserve historical results; these legacy seed URLs have no confirmed live feed.
INSERT INTO audit_events(actor_subject,action,entity_type,entity_id,metadata,occurred_at)
SELECT 'system:migration027','crawler.source_paused','crawler_source',id,
 JSON_OBJECT('url',base_url,'reason','Feed inicial sem operação confirmada; revalidar antes de reativar.'),UTC_TIMESTAMP(6)
FROM crawler_sources WHERE status='active' AND base_url IN (
 'https://belemrestaurantes.com.br/sitemap.xml',
 'https://turismo.santarem.pa.gov.br/gastronomia/rss',
 'https://castanhal.pa.gov.br/sitemap-gastronomia.xml',
 'https://marajo.tur.br/onde-comer/sitemap.xml');
UPDATE crawler_sources SET status='paused',last_error='Feed inicial sem operação confirmada; revalidar antes de reativar.'
WHERE status='active' AND base_url IN (
 'https://belemrestaurantes.com.br/sitemap.xml',
 'https://turismo.santarem.pa.gov.br/gastronomia/rss',
 'https://castanhal.pa.gov.br/sitemap-gastronomia.xml',
 'https://marajo.tur.br/onde-comer/sitemap.xml');
