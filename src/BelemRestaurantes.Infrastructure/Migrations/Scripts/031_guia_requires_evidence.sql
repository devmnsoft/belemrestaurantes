-- A migração 030 marcou todo publicado como Guia. Guia exige evidência
-- (dono, horário, cardápio ou foto). Sem isso, o cadastro volta a ficha.
-- O status publicado não muda.
UPDATE restaurants AS r
SET r.catalog_tier = 'ficha'
WHERE r.catalog_tier = 'guia'
  AND NOT EXISTS (SELECT 1 FROM restaurant_owners o WHERE o.restaurant_id = r.id)
  AND NOT EXISTS (SELECT 1 FROM restaurant_schedules s WHERE s.restaurant_id = r.id)
  AND NOT EXISTS (SELECT 1 FROM restaurant_operation_info i WHERE i.restaurant_id = r.id)
  AND NOT EXISTS (SELECT 1 FROM menu_items m WHERE m.restaurant_id = r.id)
  AND (r.photo_url IS NULL OR r.photo_url = '');
