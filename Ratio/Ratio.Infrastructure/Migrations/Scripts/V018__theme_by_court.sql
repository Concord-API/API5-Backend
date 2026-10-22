CREATE MATERIALIZED VIEW dw.theme_by_court AS
SELECT
    bts.theme_sk,
    c.court_code,
    count(DISTINCT ccr.case_sk) FILTER (
        WHERE ccr.polarity_reference = 'pretensao_autor'
          AND o.outcome_code IN ('Granted', 'PartiallyGranted')
    ) AS claim_upheld_count,
    count(DISTINCT ccr.case_sk) FILTER (
        WHERE ccr.polarity_reference = 'pretensao_autor'
          AND o.outcome_code = 'Denied'
    ) AS claim_rejected_count,
    count(DISTINCT ccr.case_sk) FILTER (
        WHERE ccr.polarity_reference = 'pretensao_recorrente'
          AND o.outcome_code IN ('Granted', 'PartiallyGranted')
    ) AS appeal_upheld_count,
    count(DISTINCT ccr.case_sk) FILTER (
        WHERE ccr.polarity_reference = 'pretensao_recorrente'
          AND o.outcome_code = 'Denied'
    ) AS appeal_rejected_count
FROM dw.bridge_theme_subject bts
JOIN dw.bridge_case_subject bcs ON bcs.subject_sk = bts.subject_sk
JOIN dw.case_current_result ccr ON ccr.case_sk = bcs.case_sk
JOIN dw.dim_court c ON c.court_sk = ccr.court_sk
JOIN dw.dim_decision_outcome o ON o.outcome_sk = ccr.outcome_sk
GROUP BY bts.theme_sk, c.court_code
WITH NO DATA;

CREATE UNIQUE INDEX idx_theme_by_court_pk ON dw.theme_by_court (theme_sk, court_code);
