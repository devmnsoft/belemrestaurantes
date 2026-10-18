-- Filter and order the page IDs before loading observations and duplicate checks.
CREATE INDEX ix_restaurant_review_date ON restaurants(status,moderation_stage,created_at,id);
CREATE INDEX ix_restaurant_review_name ON restaurants(status,moderation_stage,name,id);
