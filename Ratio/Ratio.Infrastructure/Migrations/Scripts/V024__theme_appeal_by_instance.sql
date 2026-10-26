CREATE MATERIALIZED VIEW dw.theme_appeal_by_instance AS
SELECT
    bts.theme_sk,
    CASE WHEN c.court_code IN ('STJ', 'STF') THEN 'superior' ELSE 'state' END AS instance,
    count(DISTINCT ccr.case_sk) AS judged_count,
    count(DISTINCT ccr.case_sk) FILTER (WHERE o.outcome_code = 'Granted') AS upheld_count,
    count(DISTINCT ccr.case_sk) FILTER (WHERE o.outcome_code = 'PartiallyGranted') AS partially_upheld_count,
    count(DISTINCT ccr.case_sk) FILTER (WHERE o.outcome_code = 'Denied') AS rejected_count
FROM dw.bridge_theme_subject bts
JOIN dw.bridge_case_subject bcs ON bcs.subject_sk = bts.subject_sk
JOIN dw.case_current_result ccr ON ccr.case_sk = bcs.case_sk
JOIN dw.dim_court c ON c.court_sk = ccr.court_sk
JOIN dw.dim_decision_outcome o ON o.outcome_sk = ccr.outcome_sk
WHERE ccr.polarity_reference = 'pretensao_recorrente'
GROUP BY bts.theme_sk, CASE WHEN c.court_code IN ('STJ', 'STF') THEN 'superior' ELSE 'state' END
WITH NO DATA;

CREATE UNIQUE INDEX idx_theme_appeal_by_instance_pk ON dw.theme_appeal_by_instance (theme_sk, instance);
