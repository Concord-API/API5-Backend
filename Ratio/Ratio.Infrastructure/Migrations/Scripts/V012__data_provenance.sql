CREATE MATERIALIZED VIEW dw.data_provenance AS
SELECT 'cases'::text AS block, source, max(extracted_at) AS extracted_at, count(DISTINCT case_sk) AS row_count
FROM dw.fact_case_event
GROUP BY source
UNION ALL
SELECT 'doctrine'::text AS block, source, max(extracted_at) AS extracted_at, count(*) AS row_count
FROM dw.dim_doctrine
GROUP BY source
WITH NO DATA;

CREATE UNIQUE INDEX idx_data_provenance_pk ON dw.data_provenance (block, source);
