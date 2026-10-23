CREATE MATERIALIZED VIEW dw.theme_court_activity AS
SELECT
    bts.theme_sk,
    c.court_code,
    count(DISTINCT ccr.case_sk) AS decision_count,
    max(d.full_date) AS last_decision_date
FROM dw.bridge_theme_subject bts
JOIN dw.bridge_case_subject bcs ON bcs.subject_sk = bts.subject_sk
JOIN dw.case_current_result ccr ON ccr.case_sk = bcs.case_sk
JOIN dw.dim_court c ON c.court_sk = ccr.court_sk
LEFT JOIN dw.dim_date d ON d.date_sk = ccr.date_sk
GROUP BY bts.theme_sk, c.court_code
WITH NO DATA;

CREATE UNIQUE INDEX idx_theme_court_activity_pk ON dw.theme_court_activity (theme_sk, court_code);
