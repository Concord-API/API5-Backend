CREATE MATERIALIZED VIEW dw.theme_outcome_distribution AS
SELECT
    bts.theme_sk,
    ccr.polarity_reference,
    count(DISTINCT ccr.case_sk) AS judged_count,
    count(DISTINCT ccr.case_sk) FILTER (WHERE o.outcome_code = 'Granted') AS upheld_count,
    count(DISTINCT ccr.case_sk) FILTER (WHERE o.outcome_code = 'PartiallyGranted') AS partially_upheld_count,
    count(DISTINCT ccr.case_sk) FILTER (WHERE o.outcome_code = 'Denied') AS rejected_count
FROM dw.bridge_theme_subject bts
JOIN dw.bridge_case_subject bcs ON bcs.subject_sk = bts.subject_sk
JOIN dw.case_current_result ccr ON ccr.case_sk = bcs.case_sk
JOIN dw.dim_decision_outcome o ON o.outcome_sk = ccr.outcome_sk
GROUP BY bts.theme_sk, ccr.polarity_reference
WITH NO DATA;

CREATE UNIQUE INDEX idx_theme_outcome_distribution_pk ON dw.theme_outcome_distribution (theme_sk, polarity_reference);
