ALTER TABLE suggestions
    ADD COLUMN dedupe_key CHAR(64) NULL,
    ADD INDEX ix_suggestions_dedupe (dedupe_key, status);
