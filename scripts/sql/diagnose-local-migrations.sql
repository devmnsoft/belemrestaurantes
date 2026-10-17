-- Diagnóstico SOMENTE LEITURA para MariaDB local 10.11 / MySQL 8.
-- Executar conectado ao banco belrestaurante no HeidiSQL.
-- Não executa DDL nem modifica schema_migrations.
USE belrestaurante;
SELECT VERSION() AS database_version, DATABASE() AS active_database;
SELECT id,name,applied_at FROM schema_migrations ORDER BY id;
SELECT id,checksum,started_at,completed_at FROM schema_migration_attempts ORDER BY id;
SELECT TABLE_NAME,ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA='belrestaurante'
AND TABLE_NAME IN ('restaurants','admin_users','member_users','user_experiences','restaurant_review_replies','gastronomic_trails','gastronomic_trail_stops','restaurant_claim_requests')
ORDER BY TABLE_NAME;
SELECT TABLE_NAME,COLUMN_NAME,COLUMN_TYPE,IS_NULLABLE,COLUMN_KEY FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA='belrestaurante' AND
((TABLE_NAME IN ('restaurants','member_users','user_experiences') AND COLUMN_NAME='id')
OR (TABLE_NAME='restaurant_review_replies' AND COLUMN_NAME IN ('restaurant_id','experience_id')))
ORDER BY TABLE_NAME,COLUMN_NAME;
SELECT TABLE_NAME,CONSTRAINT_NAME,REFERENCED_TABLE_NAME,REFERENCED_COLUMN_NAME
FROM information_schema.KEY_COLUMN_USAGE
WHERE TABLE_SCHEMA='belrestaurante' AND REFERENCED_TABLE_NAME IS NOT NULL
AND TABLE_NAME IN ('user_experiences','restaurant_review_replies','gastronomic_trail_stops')
ORDER BY TABLE_NAME,CONSTRAINT_NAME;
-- Para detalhes do erro 150, executar SHOW ENGINE INNODB STATUS; e abrir o campo Status completo.
