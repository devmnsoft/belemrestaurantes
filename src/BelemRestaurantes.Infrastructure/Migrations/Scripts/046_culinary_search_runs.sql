-- Index for culinary task observability. Search reservations are created in migration 045.
CREATE INDEX ix_culinary_last_status ON culinary_tasks(last_status,updated_at);
