CREATE MATERIALIZED VIEW dw.theme_by_year AS
SELECT
    bts.theme_sk,
    d.year,
    count(DISTINCT ccr.case_sk) FILTER (
        WHERE ccr.polarity_reference = 'pretensao_autor'
          AND ts.agreement_basis = 'pretensao_autor'
          AND o.outcome_code IN ('Granted', 'PartiallyGranted')
    ) AS claim_upheld_count,
    count(DISTINCT ccr.case_sk) FILTER (
        WHERE ccr.polarity_reference = 'pretensao_autor'
          AND ts.agreement_basis = 'pretensao_autor'
          AND o.outcome_code = 'Denied'
    ) AS claim_rejected_count
FROM dw.bridge_theme_subject bts
JOIN dw.bridge_case_subject bcs ON bcs.subject_sk = bts.subject_sk
JOIN dw.case_current_result ccr ON ccr.case_sk = bcs.case_sk
JOIN dw.dim_date d ON d.date_sk = ccr.date_sk
JOIN dw.dim_decision_outcome o ON o.outcome_sk = ccr.outcome_sk
JOIN dw.theme_strength ts ON ts.theme_sk = bts.theme_sk
GROUP BY bts.theme_sk, d.year
WITH NO DATA;

CREATE UNIQUE INDEX idx_theme_by_year_pk ON dw.theme_by_year (theme_sk, year);
