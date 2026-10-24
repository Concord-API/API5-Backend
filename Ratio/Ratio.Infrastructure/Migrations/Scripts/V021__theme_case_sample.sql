CREATE VIEW dw.theme_case_sample AS
SELECT DISTINCT
    t.theme_sk,
    t.theme_key,
    dc.case_sk,
    dc.case_number,
    dc.case_number_formatted,
    c.court_code,
    c.court_name,
    jb.body_name AS judging_body,
    cc.class_name AS case_class,
    dc.court_level,
    d.full_date AS decision_date,
    o.outcome_code,
    o.outcome_label,
    ccr.polarity_reference,
    dc.secrecy_level,
    dc.source,
    dc.source_link,
    dc.source_link_type
FROM dw.dim_theme t
JOIN dw.bridge_theme_subject bts ON bts.theme_sk = t.theme_sk
JOIN dw.bridge_case_subject bcs ON bcs.subject_sk = bts.subject_sk
JOIN dw.case_current_result ccr ON ccr.case_sk = bcs.case_sk
JOIN dw.dim_case dc ON dc.case_sk = ccr.case_sk
JOIN dw.dim_court c ON c.court_sk = ccr.court_sk
JOIN dw.dim_decision_outcome o ON o.outcome_sk = ccr.outcome_sk
LEFT JOIN dw.dim_judging_body jb ON jb.judging_body_sk = ccr.judging_body_sk
LEFT JOIN dw.dim_case_class cc ON cc.case_class_sk = dc.case_class_sk
LEFT JOIN dw.dim_date d ON d.date_sk = ccr.date_sk;
