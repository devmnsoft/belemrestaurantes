CREATE TABLE IF NOT EXISTS search_result_cache (
 query_hash CHAR(64) NOT NULL PRIMARY KEY,
 result_json LONGTEXT NOT NULL,
 expires_at DATETIME(6) NOT NULL,
 updated_at DATETIME(6) NOT NULL,
 KEY ix_search_expiry(expires_at)
);
CREATE TABLE IF NOT EXISTS search_topic_daily (
 topic VARCHAR(80) NOT NULL,
 search_day DATE NOT NULL,
 hits BIGINT NOT NULL DEFAULT 1,
 PRIMARY KEY(topic,search_day)
);
