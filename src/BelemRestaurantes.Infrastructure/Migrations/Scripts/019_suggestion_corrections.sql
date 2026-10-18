-- name: vinculo seguro de sugestoes de correcao com o cadastro original
ALTER TABLE suggestions
  ADD COLUMN target_restaurant_id BIGINT UNSIGNED NULL,
  ADD KEY ix_suggestion_target (target_restaurant_id),
  ADD CONSTRAINT fk_suggestion_target FOREIGN KEY (target_restaurant_id) REFERENCES restaurants(id) ON DELETE CASCADE;
