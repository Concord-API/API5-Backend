CREATE VIEW dw.theme_case_sample AS
WITH theme_cases AS (
    SELECT DISTINCT
        t.theme_sk,
        t.theme_key,
        bcs.case_sk
    FROM dw.dim_theme t
    JOIN dw.bridge_theme_subject bts ON bts.theme_sk = t.theme_sk
    JOIN dw.bridge_case_subject bcs ON bcs.subject_sk = bts.subject_sk
),
judged AS (
    SELECT
        tc.theme_sk,
        tc.theme_key,
        ccr.case_sk,
        ccr.judging_body_sk,
        ccr.date_sk,
        ccr.polarity_reference,
        c.court_code,
        c.court_name,
        o.outcome_code,
        o.outcome_label
    FROM theme_cases tc
    JOIN dw.case_current_result ccr ON ccr.case_sk = tc.case_sk
    JOIN dw.dim_court c ON c.court_sk = ccr.court_sk
    JOIN dw.dim_decision_outcome o ON o.outcome_sk = ccr.outcome_sk
),
case_details AS (
    SELECT
        dc.case_sk,
        dc.case_number,
        dc.case_number_formatted,
        cc.class_name AS case_class,
        dc.court_level,
        dc.secrecy_level,
        dc.source,
        dc.source_link,
        dc.source_link_type
    FROM dw.dim_case dc
    LEFT JOIN dw.dim_case_class cc ON cc.case_class_sk = dc.case_class_sk
)
SELECT
    j.theme_sk,
    j.theme_key,
    j.case_sk,
    cd.case_number,
    cd.case_number_formatted,
    j.court_code,
    j.court_name,
    jb.body_name AS judging_body,
    cd.case_class,
    cd.court_level,
    d.full_date AS decision_date,
    j.outcome_code,
    j.outcome_label,
    j.polarity_reference,
    cd.secrecy_level,
    cd.source,
    cd.source_link,
    cd.source_link_type
FROM judged j
JOIN case_details cd ON cd.case_sk = j.case_sk
LEFT JOIN dw.dim_judging_body jb ON jb.judging_body_sk = j.judging_body_sk
LEFT JOIN dw.dim_date d ON d.date_sk = j.date_sk;
