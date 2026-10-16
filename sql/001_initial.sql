BEGIN;
CREATE TABLE IF NOT EXISTS public.restaurants(
 id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
 name VARCHAR(160) NOT NULL, city VARCHAR(100) NOT NULL, neighborhood VARCHAR(120),category VARCHAR(100),address VARCHAR(350),
 website TEXT,instagram TEXT,facebook TEXT,tiktok TEXT,youtube TEXT,photo_url TEXT,
 status VARCHAR(20) NOT NULL DEFAULT 'pending' CHECK (status IN ('pending','published','rejected','suspended')),
 google_place_id TEXT,created_source_url TEXT,created_at TIMESTAMPTZ NOT NULL DEFAULT now(),updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),last_seen_at TIMESTAMPTZ,
 CONSTRAINT restaurant_name_nonempty CHECK(length(trim(name))>=2),
 CONSTRAINT safe_photo_url CHECK(photo_url IS NULL OR photo_url ~ '^https://'),
 CONSTRAINT source_id_nonempty CHECK(google_place_id IS NULL OR length(google_place_id)>2)
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_restaurants_google_place_id ON public.restaurants(google_place_id) WHERE google_place_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_restaurants_published_city ON public.restaurants(city,name) WHERE status='published';
CREATE TABLE IF NOT EXISTS public.suggestions(id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,name VARCHAR(160) NOT NULL,city VARCHAR(100) NOT NULL,website TEXT,notes VARCHAR(500),status VARCHAR(20) NOT NULL DEFAULT 'pending' CHECK(status IN ('pending','approved','rejected')),submitted_at TIMESTAMPTZ NOT NULL DEFAULT now());
CREATE TABLE IF NOT EXISTS public.sync_runs(id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,source VARCHAR(60) NOT NULL,city VARCHAR(100) NOT NULL,state VARCHAR(30) NOT NULL,item_count INT NOT NULL DEFAULT 0,error_message VARCHAR(500),created_at TIMESTAMPTZ NOT NULL DEFAULT now());
CREATE TABLE IF NOT EXISTS public.media_assets(id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,restaurant_id BIGINT NOT NULL REFERENCES public.restaurants(id),object_key TEXT NOT NULL,alt_text VARCHAR(250) NOT NULL,license_type VARCHAR(80) NOT NULL,permission_reference TEXT NOT NULL,moderation_status VARCHAR(20) NOT NULL DEFAULT 'pending',created_at TIMESTAMPTZ NOT NULL DEFAULT now());
CREATE TABLE IF NOT EXISTS public.ratings(id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,restaurant_id BIGINT NOT NULL REFERENCES public.restaurants(id),user_subject VARCHAR(160) NOT NULL,stars SMALLINT NOT NULL CHECK(stars BETWEEN 1 AND 5),review_text VARCHAR(2000),status VARCHAR(20) NOT NULL DEFAULT 'pending',created_at TIMESTAMPTZ NOT NULL DEFAULT now(),UNIQUE(restaurant_id,user_subject));
CREATE TABLE IF NOT EXISTS public.audit_events(id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,actor_subject VARCHAR(160),action VARCHAR(80) NOT NULL,entity_type VARCHAR(80) NOT NULL,entity_id BIGINT,metadata JSONB NOT NULL DEFAULT '{}',occurred_at TIMESTAMPTZ NOT NULL DEFAULT now());
CREATE INDEX IF NOT EXISTS ix_sync_created ON public.sync_runs(created_at DESC);
COMMIT;