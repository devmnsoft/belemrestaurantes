ALTER TABLE crawler_sources ADD COLUMN content_only BOOLEAN NOT NULL DEFAULT FALSE;

CREATE TABLE IF NOT EXISTS external_content (
 id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
 title VARCHAR(220) NOT NULL,
 url VARCHAR(1800) NOT NULL,
 url_hash CHAR(64) NOT NULL,
 publisher VARCHAR(180) NOT NULL,
 city VARCHAR(120) NOT NULL,
 summary VARCHAR(900) NOT NULL,
 facts_json LONGTEXT NULL,
 kind VARCHAR(24) NOT NULL DEFAULT 'article',
 video_url VARCHAR(1800) NULL,
 published_at DATETIME(6) NULL,
 observed_at DATETIME(6) NOT NULL,
 state VARCHAR(24) NOT NULL DEFAULT 'published',
 updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 UNIQUE KEY uq_external_url(url_hash),
 KEY ix_external_state_city(state,city,observed_at)
);

-- Original descriptions only; no article body or third-party image is copied.
INSERT IGNORE INTO external_content(title,url,url_hash,publisher,city,summary,facts_json,observed_at)
SELECT LEFT(CONCAT(place_name,' · ',publisher),220),source_url,UPPER(SHA2(source_url,256)),publisher,city,
 CONCAT('Referência externa sobre ',place_name,' em ',city,'. Consulte a publicação original para os detalhes e confirme a atualidade das informações.'),JSON_SET(facts_json,'$.PlaceName',place_name),observed_at
FROM place_reference_index WHERE status='approved' AND expires_at>UTC_TIMESTAMP(6)
 AND source_url LIKE 'https://%' AND CHAR_LENGTH(source_url)<=1800 AND place_name NOT LIKE 'fixture%' AND publisher NOT LIKE '%teste%' AND publisher NOT LIKE '%fixture%' AND source_url NOT LIKE 'https://example.%'
ORDER BY id;
