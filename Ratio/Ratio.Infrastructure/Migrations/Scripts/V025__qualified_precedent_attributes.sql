ALTER TABLE dw.dim_precedent
    ADD COLUMN precedent_type text
    CHECK (precedent_type IN ('sumula', 'tema_repetitivo', 'irdr', 'iac', 'repercussao_geral'));

UPDATE dw.dim_precedent
SET precedent_type = 'tema_repetitivo'
WHERE precedent_kind = 'Tema';
