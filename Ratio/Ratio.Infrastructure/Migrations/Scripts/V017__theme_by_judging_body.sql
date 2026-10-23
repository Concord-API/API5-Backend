CREATE MATERIALIZED VIEW dw.theme_by_judging_body AS
WITH judged AS (
    SELECT
        bts.theme_sk,
        c.court_code,
        jb.body_name,
        public.unaccent(lower(jb.body_name)) AS plain_name,
        ccr.case_sk,
        ccr.polarity_reference,
        o.outcome_code
    FROM dw.bridge_theme_subject bts
    JOIN dw.bridge_case_subject bcs ON bcs.subject_sk = bts.subject_sk
    JOIN dw.case_current_result ccr ON ccr.case_sk = bcs.case_sk
    JOIN dw.dim_court c ON c.court_sk = ccr.court_sk
    JOIN dw.dim_decision_outcome o ON o.outcome_sk = ccr.outcome_sk
    LEFT JOIN dw.dim_judging_body jb ON jb.judging_body_sk = ccr.judging_body_sk
)
SELECT
    theme_sk,
    court_code,
    body_name,
    coalesce(
        plain_name ~ '(camara|turma|secao|orgao especial|corte especial|plenario)'
        AND plain_name !~ '(recursal|gabinete|\mgab\M|vara|presidencia|juizado)',
        false
    ) AS is_panel,
    count(DISTINCT case_sk) FILTER (
        WHERE polarity_reference = 'pretensao_autor'
          AND outcome_code IN ('Granted', 'PartiallyGranted')
    ) AS claim_upheld_count,
    count(DISTINCT case_sk) FILTER (
        WHERE polarity_reference = 'pretensao_autor'
          AND outcome_code = 'Denied'
    ) AS claim_rejected_count,
    count(DISTINCT case_sk) FILTER (
        WHERE polarity_reference = 'pretensao_recorrente'
          AND outcome_code IN ('Granted', 'PartiallyGranted')
    ) AS appeal_upheld_count,
    count(DISTINCT case_sk) FILTER (
        WHERE polarity_reference = 'pretensao_recorrente'
          AND outcome_code = 'Denied'
    ) AS appeal_rejected_count
FROM judged
GROUP BY theme_sk, court_code, body_name, plain_name
WITH NO DATA;

CREATE UNIQUE INDEX idx_theme_by_judging_body_pk
    ON dw.theme_by_judging_body (theme_sk, court_code, body_name) NULLS NOT DISTINCT;
