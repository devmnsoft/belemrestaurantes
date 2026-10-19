-- Numeric coordinates already exist; no geometry/SRID conversion of unverified data.
-- Separate indexes support city/cursor and viewport range searches. Validate EXPLAIN on production distribution.
CREATE INDEX ix_map_city_cursor ON restaurants(status, normalized_city, id);
CREATE INDEX ix_map_viewport ON restaurants(status, latitude, longitude, id);
