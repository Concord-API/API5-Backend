CREATE VIEW dw.theme_provenance AS
WITH theme_cases AS (
    SELECT DISTINCT bts.theme_sk, bcs.case_sk
    FROM dw.bridge_theme_subject bts
    JOIN dw.bridge_case_subject bcs ON bcs.subject_sk = bts.subject_sk
),
theme_doctrine AS (
    SELECT DISTINCT bts.theme_sk, bsd.doctrine_sk
    FROM dw.bridge_theme_subject bts
    JOIN dw.bridge_subject_doctrine bsd ON bsd.subject_sk = bts.subject_sk
),
sources AS (
    SELECT tc.theme_sk, 'cases'::text AS block, f.source,
           max(f.extracted_at) AS extracted_at, count(DISTINCT f.case_sk) AS row_count
    FROM theme_cases tc
    JOIN dw.fact_case_event f ON f.case_sk = tc.case_sk
    GROUP BY tc.theme_sk, f.source
    UNION ALL
    SELECT td.theme_sk, 'doctrine'::text AS block, d.source,
           max(d.extracted_at) AS extracted_at, count(*) AS row_count
    FROM theme_doctrine td
    JOIN dw.dim_doctrine d ON d.doctrine_sk = td.doctrine_sk
    GROUP BY td.theme_sk, d.source
)
SELECT s.theme_sk, s.block, s.source, s.extracted_at, s.row_count, cfg.methodology_version
FROM sources s
LEFT JOIN dw.strength_config cfg ON true;
