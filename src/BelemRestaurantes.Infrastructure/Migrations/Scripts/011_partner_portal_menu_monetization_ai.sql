-- name: portal do restaurante parceiro, cardapio digital inteligente, monetizacao e cuia ia
-- Requer MySQL 8.0+

-- 1. Solicitacoes de reivindicacao de estabelecimentos
CREATE TABLE IF NOT EXISTS restaurant_claim_requests (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  user_id VARCHAR(64) NOT NULL,
  business_name VARCHAR(200) NOT NULL,
  tax_id VARCHAR(30) NULL, -- CNPJ ou documento de identificacao
  contact_phone VARCHAR(50) NOT NULL,
  contact_email VARCHAR(180) NOT NULL,
  official_channels VARCHAR(500) NULL,
  verification_evidence TEXT NOT NULL,
  status ENUM('pending', 'approved', 'rejected') NOT NULL DEFAULT 'pending',
  rejection_reason VARCHAR(500) NULL,
  reviewed_by VARCHAR(64) NULL,
  reviewed_at DATETIME(6) NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  KEY ix_claim_restaurant (restaurant_id),
  KEY ix_claim_user (user_id),
  KEY ix_claim_status (status),
  CONSTRAINT fk_claim_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 2. Vinculo ativo entre comerciantes e estabelecimentos
CREATE TABLE IF NOT EXISTS restaurant_owners (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  user_id VARCHAR(64) NOT NULL,
  role VARCHAR(30) NOT NULL DEFAULT 'owner', -- owner, manager
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  UNIQUE KEY uq_owner_restaurant_user (restaurant_id, user_id),
  KEY ix_owner_user (user_id),
  CONSTRAINT fk_owner_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 3. Perfil comercial estendido do restaurante parceiro
CREATE TABLE IF NOT EXISTS restaurant_profiles (
  restaurant_id BIGINT UNSIGNED PRIMARY KEY,
  official_description TEXT NULL,
  slogan VARCHAR(250) NULL,
  history TEXT NULL,
  commercial_phone VARCHAR(50) NULL,
  commercial_whatsapp VARCHAR(50) NULL,
  commercial_email VARCHAR(180) NULL,
  delivery_whatsapp VARCHAR(50) NULL,
  delivery_ifood_url VARCHAR(350) NULL,
  delivery_custom_url VARCHAR(350) NULL,
  is_accepting_orders BOOLEAN NOT NULL DEFAULT TRUE,
  has_wifi BOOLEAN NOT NULL DEFAULT FALSE,
  has_parking BOOLEAN NOT NULL DEFAULT FALSE,
  has_outdoor_seating BOOLEAN NOT NULL DEFAULT FALSE,
  is_pet_friendly BOOLEAN NOT NULL DEFAULT FALSE,
  is_accessible BOOLEAN NOT NULL DEFAULT FALSE,
  updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  CONSTRAINT fk_profile_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 4. Horarios regulares de funcionamento (0=Domingo, 1=Segunda, ..., 6=Sabado)
CREATE TABLE IF NOT EXISTS restaurant_schedules (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  day_of_week TINYINT UNSIGNED NOT NULL, -- 0 a 6
  is_closed BOOLEAN NOT NULL DEFAULT FALSE,
  open_time TIME NULL,
  close_time TIME NULL,
  UNIQUE KEY uq_schedule_day (restaurant_id, day_of_week),
  CONSTRAINT fk_schedule_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 5. Horarios especiais e feriados (Cirio, Natal, feriados municipais paraenses)
CREATE TABLE IF NOT EXISTS restaurant_special_schedules (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  specific_date DATE NOT NULL,
  is_closed BOOLEAN NOT NULL DEFAULT FALSE,
  open_time TIME NULL,
  close_time TIME NULL,
  reason VARCHAR(150) NOT NULL,
  UNIQUE KEY uq_special_date (restaurant_id, specific_date),
  CONSTRAINT fk_special_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 6. Categorias do cardapio digital
CREATE TABLE IF NOT EXISTS menu_categories (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  name VARCHAR(100) NOT NULL,
  display_order INT NOT NULL DEFAULT 0,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  KEY ix_menu_cat_restaurant (restaurant_id, display_order),
  CONSTRAINT fk_menucat_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 7. Itens do cardapio digital inteligente
CREATE TABLE IF NOT EXISTS menu_items (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  category_id BIGINT UNSIGNED NOT NULL,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  name VARCHAR(150) NOT NULL,
  description TEXT NULL,
  price DECIMAL(10,2) NOT NULL DEFAULT 0.00,
  image_url VARCHAR(350) NULL,
  ingredients TEXT NULL,
  allergens VARCHAR(300) NULL,
  is_available BOOLEAN NOT NULL DEFAULT TRUE,
  is_regional_dish BOOLEAN NOT NULL DEFAULT FALSE,
  regional_dish_type VARCHAR(50) NULL, -- tacaca, manicoba, pato_tucupi, acai, vatapa, caruru, peixe_regional, sobremesa_paraense, outro
  display_order INT NOT NULL DEFAULT 0,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  KEY ix_menuitem_cat (category_id, display_order),
  KEY ix_menuitem_restaurant (restaurant_id),
  CONSTRAINT fk_menuitem_cat FOREIGN KEY (category_id) REFERENCES menu_categories (id) ON DELETE CASCADE,
  CONSTRAINT fk_menuitem_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 8. Eventos e novidades publicados pelo parceiro
CREATE TABLE IF NOT EXISTS restaurant_announcements (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  title VARCHAR(180) NOT NULL,
  description TEXT NOT NULL,
  image_url VARCHAR(350) NULL,
  event_date DATETIME NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  KEY ix_announcement_restaurant (restaurant_id, created_at),
  CONSTRAINT fk_announcement_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 9. Respostas oficiais dos estabelecimentos as avaliacoes dos clientes
CREATE TABLE IF NOT EXISTS restaurant_review_replies (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  experience_id BIGINT UNSIGNED NOT NULL,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  user_id VARCHAR(64) NOT NULL,
  reply_text TEXT NOT NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  UNIQUE KEY uq_reply_experience (experience_id),
  KEY ix_reply_restaurant (restaurant_id),
  CONSTRAINT fk_reply_experience FOREIGN KEY (experience_id) REFERENCES user_experiences (id) ON DELETE CASCADE,
  CONSTRAINT fk_reply_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 10. Metricas e estatisticas de visitas e engajamento
CREATE TABLE IF NOT EXISTS restaurant_stats (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  stat_date DATE NOT NULL,
  page_views INT UNSIGNED NOT NULL DEFAULT 0,
  whatsapp_clicks INT UNSIGNED NOT NULL DEFAULT 0,
  route_clicks INT UNSIGNED NOT NULL DEFAULT 0,
  menu_views INT UNSIGNED NOT NULL DEFAULT 0,
  UNIQUE KEY uq_stat_restaurant_date (restaurant_id, stat_date),
  CONSTRAINT fk_stats_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 11. Redes sociais oficiais e midias incorporadas autorizadas
CREATE TABLE IF NOT EXISTS restaurant_social_media (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  platform VARCHAR(30) NOT NULL, -- instagram, facebook, tiktok, youtube
  profile_url VARCHAR(350) NOT NULL,
  embed_code TEXT NULL,
  is_verified BOOLEAN NOT NULL DEFAULT FALSE,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  KEY ix_social_restaurant (restaurant_id),
  CONSTRAINT fk_social_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 12. Planos comerciais e monetizacao
CREATE TABLE IF NOT EXISTS commercial_plans (
  code VARCHAR(50) PRIMARY KEY,
  name VARCHAR(100) NOT NULL,
  description VARCHAR(350) NOT NULL,
  monthly_price DECIMAL(10,2) NOT NULL DEFAULT 0.00,
  badge_highlight BOOLEAN NOT NULL DEFAULT FALSE,
  menu_enabled BOOLEAN NOT NULL DEFAULT TRUE,
  priority_support BOOLEAN NOT NULL DEFAULT FALSE,
  sponsored_exposure BOOLEAN NOT NULL DEFAULT FALSE,
  multi_branch BOOLEAN NOT NULL DEFAULT FALSE,
  is_active BOOLEAN NOT NULL DEFAULT TRUE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Inserir planos padrao se nao existirem
INSERT IGNORE INTO commercial_plans (code, name, description, monthly_price, badge_highlight, menu_enabled, priority_support, sponsored_exposure, multi_branch, is_active)
VALUES
('free', 'Plano Gratuito', 'Perfil basico, localizacao, horarios regulares e avaliacoes comunitarias.', 0.00, FALSE, TRUE, FALSE, FALSE, FALSE, TRUE),
('cuia_pro', 'Plano Cuia Pro', 'Cardapio digital ilimitado, galeria ampliada, estatisticas de visitas e gestao de perfil.', 79.90, TRUE, TRUE, TRUE, FALSE, FALSE, TRUE),
('cuia_highlight', 'Plano Cuia Destaque', 'Todos os recursos Pro mais destaque editorial, campanhas patrocinadas identificadas e maior exposicao.', 149.90, TRUE, TRUE, TRUE, TRUE, FALSE, TRUE),
('cuia_networks', 'Plano Cuia Redes', 'Gestao unificada para redes e franquias com multiplos estabelecimentos e relatorios consolidados.', 299.90, TRUE, TRUE, TRUE, TRUE, TRUE, TRUE);

-- Assinaturas ativas dos estabelecimentos
CREATE TABLE IF NOT EXISTS restaurant_subscriptions (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  user_id VARCHAR(64) NOT NULL,
  plan_code VARCHAR(50) NOT NULL,
  status ENUM('active', 'pending', 'canceled', 'expired') NOT NULL DEFAULT 'active',
  started_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  expires_at DATETIME(6) NULL,
  payment_method VARCHAR(50) NOT NULL DEFAULT 'pix',
  KEY ix_sub_restaurant (restaurant_id),
  KEY ix_sub_user (user_id),
  CONSTRAINT fk_sub_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE,
  CONSTRAINT fk_sub_plan FOREIGN KEY (plan_code) REFERENCES commercial_plans (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Pedidos e pagamentos dos comerciantes (com chave PIX MNSOFT)
CREATE TABLE IF NOT EXISTS merchant_orders (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  order_number VARCHAR(64) NOT NULL,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  user_id VARCHAR(64) NOT NULL,
  plan_code VARCHAR(50) NOT NULL,
  amount DECIMAL(10,2) NOT NULL,
  payment_status ENUM('pending', 'paid', 'failed', 'refunded') NOT NULL DEFAULT 'pending',
  payment_method VARCHAR(50) NOT NULL DEFAULT 'pix',
  pix_cnpj VARCHAR(30) NOT NULL DEFAULT '18.160.057/0001-13',
  pix_copy_paste TEXT NULL,
  paid_at DATETIME(6) NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  UNIQUE KEY uq_order_number (order_number),
  KEY ix_order_restaurant (restaurant_id),
  CONSTRAINT fk_order_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Cupons de desconto e beneficios
CREATE TABLE IF NOT EXISTS restaurant_coupons (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  code VARCHAR(50) NOT NULL,
  title VARCHAR(150) NOT NULL,
  description VARCHAR(350) NOT NULL,
  discount_percentage TINYINT UNSIGNED NULL,
  discount_amount DECIMAL(10,2) NULL,
  valid_until DATE NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  KEY ix_coupon_restaurant (restaurant_id),
  CONSTRAINT fk_coupon_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 13. Mobilidade e Roteiros Gastronomicos do Para
CREATE TABLE IF NOT EXISTS gastronomic_trails (
  id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  slug VARCHAR(80) NOT NULL UNIQUE,
  title VARCHAR(150) NOT NULL,
  region VARCHAR(100) NOT NULL, -- Belem, Ilha do Combu, Icoaraci, Mosqueiro, Salinopolis, Santarem, etc.
  description TEXT NOT NULL,
  cover_image VARCHAR(350) NULL,
  estimated_duration_hours INT NOT NULL DEFAULT 4,
  transport_tips TEXT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Paradas dos roteiros gastronômicos
CREATE TABLE IF NOT EXISTS trail_stops (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  trail_id INT UNSIGNED NOT NULL,
  restaurant_id BIGINT UNSIGNED NOT NULL,
  stop_order INT NOT NULL DEFAULT 1,
  highlight_dish VARCHAR(150) NULL,
  notes VARCHAR(350) NULL,
  UNIQUE KEY uq_trail_stop (trail_id, restaurant_id),
  KEY ix_trail_stop_order (trail_id, stop_order),
  CONSTRAINT fk_stop_trail FOREIGN KEY (trail_id) REFERENCES gastronomic_trails (id) ON DELETE CASCADE,
  CONSTRAINT fk_stop_restaurant FOREIGN KEY (restaurant_id) REFERENCES restaurants (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 14. Alertas do Cuia Discovery para revisao administrativa
CREATE TABLE IF NOT EXISTS discovery_alerts (
  id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  restaurant_id BIGINT UNSIGNED NULL,
  external_id VARCHAR(255) NULL,
  alert_type ENUM('closed_down', 'address_changed', 'schedule_changed', 'conflict_detected', 'name_changed') NOT NULL,
  details JSON NOT NULL,
  status ENUM('pending', 'reviewed', 'dismissed') NOT NULL DEFAULT 'pending',
  reviewed_by VARCHAR(64) NULL,
  reviewed_at DATETIME(6) NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  KEY ix_alert_status (status),
  KEY ix_alert_restaurant (restaurant_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
