CREATE OR REPLACE FUNCTION dw.search_themes(
    p_query text,
    p_limit integer DEFAULT 20,
    p_courts text[] DEFAULT NULL,
    p_period_years integer DEFAULT NULL,
    p_levels text[] DEFAULT NULL,
    p_min_score integer DEFAULT NULL)
RETURNS TABLE (theme_key bigint, theme_name text, subject_area text, rank real, "position" bigint)
LANGUAGE sql STABLE PARALLEL SAFE AS
$$
    WITH q AS MATERIALIZED (
        SELECT websearch_to_tsquery('dw.pt_unaccent'::regconfig, dw.expand_query(p_query)) AS tsq,
               dw.norm_pt(p_query) AS norm
    ),
    ranked AS (
        SELECT t.theme_sk,
               t.theme_key,
               t.theme_name,
               t.subject_area,
               greatest(
                   ts_rank(t.search_vector, q.tsq) * 10,
                   word_similarity(q.norm, t.theme_name_norm)
               ) AS rank
        FROM dw.dim_theme t
        CROSS JOIN q
    )
    SELECT r.theme_key,
           r.theme_name,
           r.subject_area,
           r.rank,
           row_number() OVER (ORDER BY ts.score DESC, ts.judged DESC, r.theme_name) AS position
    FROM ranked r
    JOIN dw.theme_summary s ON s.theme_sk = r.theme_sk
    JOIN dw.theme_strength ts ON ts.theme_sk = r.theme_sk
    WHERE r.rank >= 0.5
      AND s.judged_case_count > 0
      AND (p_min_score IS NULL OR ts.score >= p_min_score)
      AND ((p_courts IS NULL AND p_period_years IS NULL AND p_levels IS NULL)
           OR dw.theme_matches_filters(r.theme_sk, p_courts, p_period_years, p_levels))
    ORDER BY position
    LIMIT p_limit
$$;
