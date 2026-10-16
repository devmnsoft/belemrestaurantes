BEGIN;
CREATE TABLE IF NOT EXISTS public.discovery_targets(
 id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
 city VARCHAR(100) NOT NULL,
 category VARCHAR(100) NOT NULL,
 enabled BOOLEAN NOT NULL DEFAULT true,
 next_run_at TIMESTAMPTZ NOT NULL DEFAULT now(),
 locked_until TIMESTAMPTZ,
 attempts INTEGER NOT NULL DEFAULT 0,
 last_success_at TIMESTAMPTZ,
 last_error VARCHAR(500),
 UNIQUE(city,category)
);
CREATE INDEX IF NOT EXISTS idx_discovery_due ON public.discovery_targets(next_run_at) WHERE enabled=true;
CREATE TABLE IF NOT EXISTS public.source_observations(
 id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
 restaurant_id BIGINT REFERENCES public.restaurants(id),
 provider VARCHAR(40) NOT NULL,
 external_id VARCHAR(250) NOT NULL,
 source_url TEXT,
 first_seen_at TIMESTAMPTZ NOT NULL DEFAULT now(),
 last_seen_at TIMESTAMPTZ NOT NULL DEFAULT now(),
 data_quality VARCHAR(20) NOT NULL DEFAULT 'unverified',
 UNIQUE(provider,external_id)
);
CREATE TABLE IF NOT EXISTS public.moderation_queue(
 id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
 item_type VARCHAR(30) NOT NULL,
 item_id BIGINT NOT NULL,
 reason VARCHAR(240) NOT NULL,
 status VARCHAR(20) NOT NULL DEFAULT 'pending',
 created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
 reviewed_at TIMESTAMPTZ
);
INSERT INTO public.discovery_targets(city,category)
SELECT c.city,k.category FROM
(VALUES ('Belém'),('Ananindeua'),('Marabá'),('Santarém'),('Salinópolis'),('Castanhal'),('Paragominas'),('Canaã dos Carajás'),('Barcarena'),('Bragança'),('Abaetetuba'),('Altamira'),('Tucuruí'),('Parauapebas'),('Cametá'),('Benevides'),('Marituba'),('Santa Izabel do Pará'),('Vigia'),('Capanema')) c(city)
CROSS JOIN (VALUES ('restaurante'),('pizzaria'),('lanchonete'),('açaí'),('cafeteria'),('tacacá')) k(category)
ON CONFLICT(city,category) DO NOTHING;
COMMIT;