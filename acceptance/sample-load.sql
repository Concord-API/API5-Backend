BEGIN;

INSERT INTO dw.strength_config (methodology_version, reference_year) VALUES ('1.0', 2026);

INSERT INTO dw.dim_decision_outcome (outcome_code, outcome_label, counts_in_metric) VALUES
    ('Granted', 'Procedente', true),
    ('Denied', 'Improcedente', true);

INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified, polarity_reference)
SELECT m.code, m.name, o.outcome_sk, true, m.polarity
FROM (VALUES
    (219, 'Procedência', 'Granted', 'pretensao_autor'),
    (220, 'Improcedência', 'Denied', 'pretensao_autor'),
    (237, 'Provimento', 'Granted', 'pretensao_recorrente'),
    (239, 'Não provimento', 'Denied', 'pretensao_recorrente')
) AS m (code, name, outcome, polarity)
JOIN dw.dim_decision_outcome o ON o.outcome_code = m.outcome;

INSERT INTO dw.dim_court (court_code, court_name, state_uf) VALUES
    ('TJSP', 'Tribunal de Justiça de São Paulo', 'SP'),
    ('TJRJ', 'Tribunal de Justiça do Rio de Janeiro', 'RJ'),
    ('TJMG', 'Tribunal de Justiça de Minas Gerais', 'MG');

INSERT INTO dw.dim_case_class (class_name, claimant_type) VALUES ('Procedimento Comum Cível', 'autor_particular');

INSERT INTO dw.dim_date (date_sk, full_date, year, quarter, month, month_name, day) VALUES
    (20210310, '2021-03-10', 2021, 1, 3, 'Março', 10),
    (20220815, '2022-08-15', 2022, 3, 8, 'Agosto', 15),
    (20230520, '2023-05-20', 2023, 2, 5, 'Maio', 20),
    (20240615, '2024-06-15', 2024, 2, 6, 'Junho', 15),
    (20250902, '2025-09-02', 2025, 3, 9, 'Setembro', 2);

INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES
    ('Cobrança de dívida', 1),
    ('Negativação após quitação', 2),
    ('Juros abusivos em contrato bancário', 3);

INSERT INTO dw.dim_subject (subject_name, subject_code)
SELECT theme_name, theme_key * 100 FROM dw.dim_theme;

INSERT INTO dw.bridge_theme_subject (theme_sk, subject_sk)
SELECT t.theme_sk, s.subject_sk
FROM dw.dim_theme t
JOIN dw.dim_subject s ON s.subject_code = t.theme_key * 100;

CREATE TEMPORARY TABLE sample_case (
    theme_key bigint,
    court_code text,
    tribunal text,
    seq int,
    date_sk int,
    upheld boolean
) ON COMMIT DROP;

INSERT INTO sample_case (theme_key, court_code, tribunal, seq, date_sk, upheld)
SELECT b.theme_key, b.court_code, b.tribunal, b.first_seq + n, b.date_sk, n <= b.upheld
FROM (VALUES
    (1, 'TJSP', '26', 0, 20230520, 12, 14),
    (1, 'TJSP', '26', 100, 20240615, 15, 16),
    (1, 'TJRJ', '19', 200, 20250902, 7, 10),
    (2, 'TJSP', '26', 300, 20210310, 4, 6),
    (2, 'TJMG', '13', 400, 20220815, 5, 8),
    (2, 'TJSP', '26', 500, 20240615, 9, 10),
    (3, 'TJRJ', '19', 600, 20230520, 3, 7),
    (3, 'TJMG', '13', 700, 20250902, 2, 5)
) AS b (theme_key, court_code, tribunal, first_seq, date_sk, upheld, total)
CROSS JOIN LATERAL generate_series(1, b.total) AS n;

INSERT INTO dw.dim_case (case_number, court_sk, case_class_sk, court_level, source, extracted_at)
SELECT lpad((1000000 + sc.seq)::text, 7, '0') || '-00.' || left(sc.date_sk::text, 4) || '.8.' || sc.tribunal || '.0100',
       c.court_sk, cc.case_class_sk, 'First', 'datajud', TIMESTAMPTZ '2026-10-01 12:00:00+00'
FROM sample_case sc
JOIN dw.dim_court c ON c.court_code = sc.court_code
CROSS JOIN dw.dim_case_class cc;

INSERT INTO dw.bridge_case_subject (case_sk, subject_sk)
SELECT dc.case_sk, s.subject_sk
FROM sample_case sc
JOIN dw.dim_case dc ON dc.case_number = lpad((1000000 + sc.seq)::text, 7, '0') || '-00.' || left(sc.date_sk::text, 4) || '.8.' || sc.tribunal || '.0100'
JOIN dw.dim_subject s ON s.subject_code = sc.theme_key * 100;

INSERT INTO dw.fact_case_event (case_sk, court_sk, movement_sk, date_sk, occurred_at, source_url, extracted_at, natural_key)
SELECT dc.case_sk, dc.court_sk, m.movement_sk, sc.date_sk, d.full_date::timestamptz,
       'https://example.org/processo/' || dc.case_number, TIMESTAMPTZ '2026-10-01 12:00:00+00', 'sample:' || dc.case_number
FROM sample_case sc
JOIN dw.dim_case dc ON dc.case_number = lpad((1000000 + sc.seq)::text, 7, '0') || '-00.' || left(sc.date_sk::text, 4) || '.8.' || sc.tribunal || '.0100'
JOIN dw.dim_date d ON d.date_sk = sc.date_sk
JOIN dw.dim_movement m ON m.movement_code = CASE WHEN sc.upheld THEN 219 ELSE 220 END;

INSERT INTO dw.theme_narrative (theme_sk, lead, body, text_origin, methodology_version, generated_at)
SELECT theme_sk,
       '[{"text":"Nas decisões julgadas, houve acolhimento da pretensão do autor."}]'::jsonb,
       '[{"text":"Amostra fixa usada no teste de aceite."}]'::jsonb,
       'template', '1.0', DATE '2026-10-01'
FROM dw.dim_theme;

REFRESH MATERIALIZED VIEW dw.case_current_result;
REFRESH MATERIALIZED VIEW dw.theme_summary;
REFRESH MATERIALIZED VIEW dw.theme_strength;
REFRESH MATERIALIZED VIEW dw.theme_outcome_distribution;
REFRESH MATERIALIZED VIEW dw.data_provenance;

COMMIT;
