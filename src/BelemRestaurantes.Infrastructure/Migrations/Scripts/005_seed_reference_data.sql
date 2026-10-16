-- name: carga inicial de municípios, bairros e categorias

INSERT IGNORE INTO cities (name, normalized_name, state) VALUES
  ('Belém', 'belem', 'PA'),
  ('Ananindeua', 'ananindeua', 'PA'),
  ('Marabá', 'maraba', 'PA'),
  ('Santarém', 'santarem', 'PA'),
  ('Salinópolis', 'salinopolis', 'PA'),
  ('Castanhal', 'castanhal', 'PA'),
  ('Paragominas', 'paragominas', 'PA'),
  ('Canaã dos Carajás', 'canaa dos carajas', 'PA'),
  ('Barcarena', 'barcarena', 'PA'),
  ('Bragança', 'braganca', 'PA'),
  ('Abaetetuba', 'abaetetuba', 'PA'),
  ('Altamira', 'altamira', 'PA'),
  ('Tucuruí', 'tucurui', 'PA'),
  ('Parauapebas', 'parauapebas', 'PA'),
  ('Cametá', 'cameta', 'PA'),
  ('Benevides', 'benevides', 'PA'),
  ('Marituba', 'marituba', 'PA'),
  ('Santa Izabel do Pará', 'santa izabel do para', 'PA'),
  ('Vigia', 'vigia', 'PA'),
  ('Capanema', 'capanema', 'PA'),
  ('Soure', 'soure', 'PA'),
  ('Oeiras do Pará', 'oeiras do para', 'PA'),
  ('Portel', 'portel', 'PA'),
  ('Aveiro', 'aveiro', 'PA'),
  ('Óbidos', 'obidos', 'PA'),
  ('Itaituba', 'itaituba', 'PA');

INSERT IGNORE INTO categories (name, slug) VALUES
  ('Restaurante', 'restaurante'),
  ('Pizzaria', 'pizzaria'),
  ('Lanchonete', 'lanchonete'),
  ('Açaí', 'acai'),
  ('Cafeteria', 'cafeteria'),
  ('Tacacá', 'tacaca'),
  ('Churrascaria', 'churrascaria'),
  ('Bar', 'bar'),
  ('Buffet', 'buffet'),
  ('Padaria', 'padaria'),
  ('Hamburgueria', 'hamburgueria'),
  ('Pastelaria', 'pastelaria');

INSERT IGNORE INTO neighborhoods (city_id, name, normalized_name)
SELECT c.id, b.name, b.normalized_name
FROM cities c
JOIN (
  SELECT 'Belém' city, 'Nazaré' name, 'nazare' normalized_name UNION ALL
  SELECT 'Belém', 'Saúde', 'saude' UNION ALL
  SELECT 'Belém', 'Boa Vista', 'boa vista' UNION ALL
  SELECT 'Belém', 'Umaré', 'umare' UNION ALL
  SELECT 'Belém', 'Reduto', 'reduto' UNION ALL
  SELECT 'Belém', 'Jardim Salvador', 'jardim salvador' UNION ALL
  SELECT 'Belém', 'Ponta da Pedreira', 'ponta da pedreira' UNION ALL
  SELECT 'Belém', 'Batista Campos', 'batista campos' UNION ALL
  SELECT 'Belém', 'São Lourenço', 'sao lorenco' UNION ALL
  SELECT 'Belém', 'Água Branca', 'agua branca' UNION ALL
  SELECT 'Belém', 'Ipiranga', 'ipiranga' UNION ALL
  SELECT 'Belém', 'Guajará', 'guajara' UNION ALL
  SELECT 'Belém', 'Marco', 'marco' UNION ALL
  SELECT 'Belém', 'Montese', 'montese' UNION ALL
  SELECT 'Belém', 'Curió', 'curio' UNION ALL
  SELECT 'Belém', 'Tuna', 'tuna' UNION ALL
  SELECT 'Belém', 'Bengui', 'bengui' UNION ALL
  SELECT 'Ananindeua', 'Central', 'central' UNION ALL
  SELECT 'Ananindeua', 'Jardim Atlântico', 'jardim atlantico' UNION ALL
  SELECT 'Castanhal', 'Centro', 'centro' UNION ALL
  SELECT 'Santarém', 'Centro', 'centro' UNION ALL
  SELECT 'Santarém', 'Fatima', 'fatima'
) b ON b.city = c.name;

-- Alvos iniciais de descoberta. enabled=FALSE: o operador habilita depois de
-- validar credenciais e orçamento, evitando consumo no primeiro arranque.
INSERT IGNORE INTO discovery_targets (city, normalized_city, category, enabled)
SELECT t.city, t.normalized_city, t.category, FALSE
FROM (
  SELECT 'Belém' city, 'belem' normalized_city, 'restaurante' category UNION ALL
  SELECT 'Belém', 'belem', 'pizzaria' UNION ALL
  SELECT 'Belém', 'belem', 'lanchonete' UNION ALL
  SELECT 'Belém', 'belem', 'acai' UNION ALL
  SELECT 'Belém', 'belem', 'cafeteria' UNION ALL
  SELECT 'Belém', 'belem', 'tacaca' UNION ALL
  SELECT 'Ananindeua', 'ananindeua', 'restaurante' UNION ALL
  SELECT 'Ananindeua', 'ananindeua', 'pizzaria' UNION ALL
  SELECT 'Ananindeua', 'ananindeua', 'acai' UNION ALL
  SELECT 'Marabá', 'maraba', 'restaurante' UNION ALL
  SELECT 'Marabá', 'maraba', 'pizzaria' UNION ALL
  SELECT 'Santarém', 'santarem', 'restaurante' UNION ALL
  SELECT 'Santarém', 'santarem', 'tacaca' UNION ALL
  SELECT 'Santarém', 'santarem', 'acai' UNION ALL
  SELECT 'Castanhal', 'castanhal', 'restaurante' UNION ALL
  SELECT 'Castanhal', 'castanhal', 'pizzaria' UNION ALL
  SELECT 'Paragominas', 'paragominas', 'restaurante' UNION ALL
  SELECT 'Soure', 'soure', 'restaurante' UNION ALL
  SELECT 'Soure', 'soure', 'acai' UNION ALL
  SELECT 'Santarém', 'santarem', 'cafeteria' UNION ALL
  SELECT 'Marabá', 'maraba', 'acai' UNION ALL
  SELECT 'Parauapebas', 'parauapebas', 'restaurante' UNION ALL
  SELECT 'Tucuruí', 'tucurui', 'restaurante' UNION ALL
  SELECT 'Cametá', 'cameta', 'tacaca' UNION ALL
  SELECT 'Bragança', 'braganca', 'restaurante' UNION ALL
  SELECT 'Altamira', 'altamira', 'restaurante' UNION ALL
  SELECT 'Canaã dos Carajás', 'canaa dos carajas', 'restaurante' UNION ALL
  SELECT 'Marituba', 'marituba', 'restaurante' UNION ALL
  SELECT 'Benevides', 'benevides', 'restaurante' UNION ALL
  SELECT 'Santa Izabel do Pará', 'santa izabel do para', 'restaurante' UNION ALL
  SELECT 'Capanema', 'capanema', 'restaurante' UNION ALL
  SELECT 'Vigia', 'vigia', 'restaurante' UNION ALL
  SELECT 'Barcarena', 'barcarena', 'restaurante' UNION ALL
  SELECT 'Abaetetuba', 'abaetetuba', 'restaurante' UNION ALL
  SELECT 'Salinópolis', 'salinopolis', 'restaurante' UNION ALL
  SELECT 'Belém', 'belem', 'churrascaria' UNION ALL
  SELECT 'Belém', 'belem', 'pastelaria'
) t;

-- Papéis administrativos. O código recusa remover o último platform_admin.
INSERT IGNORE INTO admin_roles (id, name) VALUES
  (UUID(), 'restaurant_moderator'),
  (UUID(), 'platform_operator'),
  (UUID(), 'platform_admin');

INSERT IGNORE INTO api_budgets (provider, monthly_limit_micros_usd, monthly_limit_requests, paused) VALUES
  ('Gemini', 0, 0, TRUE),
  ('Groq', 0, 0, TRUE),
  ('DeepSeek', 0, 0, TRUE),
  ('GooglePlaces', 0, 0, TRUE);