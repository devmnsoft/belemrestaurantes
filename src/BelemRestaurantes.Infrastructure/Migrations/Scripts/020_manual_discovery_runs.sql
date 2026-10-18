-- Importações pontuais têm ciclo/fonte auditáveis sem consumir um job agendado.
-- NULL identifica a execução manual; jobs existentes mantêm sua referência.
ALTER TABLE discovery_target_runs MODIFY COLUMN job_id BIGINT UNSIGNED NULL;
