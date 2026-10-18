-- Correlated moderation checks must locate the item before inspecting its reasons.
CREATE INDEX ix_moderation_item_state ON moderation_queue(item_type,item_id,status,reason);
CREATE INDEX ix_observation_latest ON source_observations(restaurant_id,last_seen_at);
