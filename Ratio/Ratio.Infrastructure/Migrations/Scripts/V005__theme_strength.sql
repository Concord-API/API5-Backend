CREATE MATERIALIZED VIEW dw.theme_strength AS
WITH base AS (
    SELECT
        s.theme_sk,
        s.theme_name,
        s.subject_area,
        s.court_count AS courts,
        s.last_decision_date,
        extract(year FROM s.last_decision_date)::smallint AS last_decision_year,
        s.dominant_claimant,
        s.claimant_breakdown,
        s.unknown_claimant_count,
        s.claim_upheld_count,
        s.claim_rejected_count,
        s.appeal_upheld_count,
        s.appeal_rejected_count,
        CASE
            WHEN s.claim_upheld_count + s.claim_rejected_count > 0 THEN 'pretensao_autor'
            WHEN s.appeal_upheld_count + s.appeal_rejected_count > 0 THEN 'pretensao_recorrente'
        END AS agreement_basis,
        CASE
            WHEN s.claim_upheld_count + s.claim_rejected_count > 0 THEN s.claim_upheld_count
            WHEN s.appeal_upheld_count + s.appeal_rejected_count > 0 THEN s.appeal_upheld_count
            ELSE 0
        END AS upheld,
        CASE
            WHEN s.claim_upheld_count + s.claim_rejected_count > 0 THEN s.claim_rejected_count
            WHEN s.appeal_upheld_count + s.appeal_rejected_count > 0 THEN s.appeal_rejected_count
            ELSE 0
        END AS rejected,
        CASE
            WHEN s.claim_upheld_count + s.claim_rejected_count > 0 THEN s.claim_polarity_label
            WHEN s.appeal_upheld_count + s.appeal_rejected_count > 0 THEN 'acolhimento da pretensão de quem recorreu'
        END AS claim_polarity_label,
        cfg.weight_agreement,
        cfg.weight_volume,
        cfg.weight_coverage,
        cfg.weight_recency,
        cfg.volume_saturation,
        cfg.coverage_courts,
        cfg.reference_year
    FROM dw.theme_summary s
    CROSS JOIN dw.strength_config cfg
),
components AS (
    SELECT
        b.*,
        (b.upheld + b.rejected) AS judged,
        CASE
            WHEN b.upheld + b.rejected = 0 THEN 0
            ELSE round(greatest(0, (greatest(b.upheld, b.rejected)::numeric / (b.upheld + b.rejected) - 0.5) * 2), 3)
        END AS agreement_value,
        CASE
            WHEN b.upheld + b.rejected = 0 THEN 0
            ELSE round(least(1, log(10::numeric, (b.upheld + b.rejected + 1)::numeric) / log(10::numeric, (b.volume_saturation + 1)::numeric)), 3)
        END AS volume_value,
        round(least(1, b.courts::numeric / b.coverage_courts), 3) AS coverage_value,
        CASE
            WHEN b.last_decision_date IS NULL THEN 0
            WHEN (b.reference_year - b.last_decision_year) <= 1 THEN 1
            WHEN (b.reference_year - b.last_decision_year) >= 6 THEN 0
            ELSE round(1 - ((b.reference_year - b.last_decision_year) - 1) / 5.0, 3)
        END AS recency_value
    FROM base b
),
scored AS (
    SELECT
        c.*,
        round((c.agreement_value * c.weight_agreement
             + c.volume_value * c.weight_volume
             + c.coverage_value * c.weight_coverage
             + c.recency_value * c.weight_recency) * 100)::integer AS score
    FROM components c
)
SELECT
    s.theme_sk,
    s.theme_name,
    s.subject_area,
    s.score,
    CASE
        WHEN s.score >= 90 THEN 'Consolidada'
        WHEN s.score >= 75 THEN 'Dominante'
        WHEN s.score >= 55 THEN 'Em formação'
        ELSE 'Divergente'
    END AS level,
    s.agreement_value,
    s.weight_agreement AS agreement_weight,
    s.volume_value,
    s.weight_volume AS volume_weight,
    s.coverage_value,
    s.weight_coverage AS coverage_weight,
    s.recency_value,
    s.weight_recency AS recency_weight,
    s.agreement_basis,
    s.dominant_claimant,
    s.claim_polarity_label,
    s.claimant_breakdown,
    s.unknown_claimant_count,
    s.judged,
    s.upheld,
    s.rejected,
    s.claim_upheld_count,
    s.claim_rejected_count,
    s.appeal_upheld_count,
    s.appeal_rejected_count,
    s.courts,
    s.last_decision_year,
    s.last_decision_date,
    s.volume_saturation,
    s.coverage_courts,
    s.reference_year
FROM scored s
WITH NO DATA;

CREATE UNIQUE INDEX idx_theme_strength_pk ON dw.theme_strength (theme_sk);
CREATE INDEX idx_theme_strength_score ON dw.theme_strength (score DESC);
