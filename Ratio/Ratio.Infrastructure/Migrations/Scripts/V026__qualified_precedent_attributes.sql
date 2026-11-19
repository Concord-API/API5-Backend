ALTER TABLE dw.dim_precedent
    ADD COLUMN precedent_type text
    CHECK (precedent_type IN ('sumula', 'tema_repetitivo', 'irdr', 'iac', 'repercussao_geral')),
    ADD COLUMN status_date date,
    ADD COLUMN suspends_cases boolean,
    ADD COLUMN leading_cases text[] NOT NULL DEFAULT '{}';

UPDATE dw.dim_precedent
SET precedent_type = 'tema_repetitivo'
WHERE precedent_kind = 'Tema';
