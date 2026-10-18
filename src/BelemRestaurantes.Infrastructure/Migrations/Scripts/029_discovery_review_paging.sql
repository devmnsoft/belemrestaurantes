-- The discovery inbox includes every pending moderation stage.
CREATE INDEX ix_restaurant_inbox_date ON restaurants(status,created_at,id);
