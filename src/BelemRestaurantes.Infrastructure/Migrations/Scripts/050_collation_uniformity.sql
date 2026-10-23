-- Uniformidade de colação (corretivo CI/MySQL 8): as migrações que criam tabelas sem
-- colação explícita herdam o padrão do servidor — utf8mb4_general_ci em MariaDB, mas
-- utf8mb4_0900_ai_ci em MySQL 8+ — e qualquer comparação entre essas tabelas e as
-- originais (utf8mb4_unicode_ci) falha no MySQL 8 com "Illegal mix of collations".
-- Converter todas para utf8mb4_unicode_ci torna o esquema idêntico nos dois engines.
ALTER TABLE admin_password_tokens CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE editorial_articles CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE editorial_review_queue CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE editorial_trail_revisions CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE external_content CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE external_free_usage CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
-- Par especial: member_organization_requests tem user_id em charset ascii (FK para
-- member_users.id, fora do escopo) e um FK duplo. Converte o pai antes do filho, com os
-- FKs desanexados, e restaura depois a definição ascii original da coluna.
ALTER TABLE member_organization_requests
  DROP FOREIGN KEY fk_org_request_member,
  DROP FOREIGN KEY fk_org_request_org;
ALTER TABLE member_organizations CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE member_organization_requests CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE member_organization_requests
  MODIFY COLUMN user_id char(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL;
ALTER TABLE member_organization_requests
  ADD CONSTRAINT fk_org_request_member FOREIGN KEY (user_id) REFERENCES member_users (id) ON DELETE CASCADE,
  ADD CONSTRAINT fk_org_request_org FOREIGN KEY (organization_id) REFERENCES member_organizations (id);
ALTER TABLE mobility_api_usage CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE mobility_fares CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE mobility_sources CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE moderation_batches CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE operational_maintenance_jobs CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE para_recipe_restaurants CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE para_recipe_versions CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE para_recipes CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE place_reference_index CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE registry_import_pages CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE research_jobs CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE restaurant_operation_history CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE restaurant_operation_info CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE schema_migration_attempts CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE search_result_cache CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE search_topic_daily CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE worker_heartbeats CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
