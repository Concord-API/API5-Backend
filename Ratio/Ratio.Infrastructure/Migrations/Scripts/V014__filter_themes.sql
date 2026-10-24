DROP FUNCTION dw.search_themes(text, integer);

DROP FUNCTION dw.top_themes(integer);

CREATE FUNCTION dw.theme_matches_filters(
    p_theme_sk bigint,
    p_courts text[],
    p_period_years integer,
    p_levels text[])
RETURNS boolean
LANGUAGE sql STABLE PARALLEL SAFE AS
$$
    SELECT EXISTS (
        SELECT 1
        FROM dw.bridge_theme_subject bts
        JOIN dw.bridge_case_subject bcs ON bcs.subject_sk = bts.subject_sk
        JOIN dw.case_current_result ccr ON ccr.case_sk = bcs.case_sk
        JOIN dw.dim_case dc ON dc.case_sk = ccr.case_sk
        JOIN dw.dim_court c ON c.court_sk = ccr.court_sk
        LEFT JOIN dw.dim_date d ON d.date_sk = ccr.date_sk
        WHERE bts.theme_sk = p_theme_sk
          AND (p_courts IS NULL OR c.court_code = ANY (p_courts))
          AND (p_levels IS NULL OR dc.court_level = ANY (p_levels))
          AND (p_period_years IS NULL
               OR d.year > (SELECT cfg.reference_year FROM dw.strength_config cfg WHERE cfg.id = 1) - p_period_years)
    )
$$;

CREATE FUNCTION dw.search_themes(
    p_query text,
    p_limit integer DEFAULT 20,
    p_courts text[] DEFAULT NULL,
    p_period_years integer DEFAULT NULL,
    p_levels text[] DEFAULT NULL,
    p_min_score integer DEFAULT NULL)
RETURNS TABLE (theme_key bigint, theme_name text, subject_area text, rank real, "position" bigint)
LANGUAGE sql STABLE PARALLEL SAFE AS
$$
    WITH ranked AS (
        SELECT t.theme_sk,
               t.theme_key,
               t.theme_name,
               t.subject_area,
               greatest(
                   ts_rank(t.search_vector, websearch_to_tsquery('dw.pt_unaccent'::regconfig, dw.expand_query(p_query))) * 10,
                   word_similarity(dw.norm_pt(p_query), t.theme_name_norm)
               ) AS rank
        FROM dw.dim_theme t
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

CREATE FUNCTION dw.top_themes(
    p_limit integer DEFAULT 20,
    p_courts text[] DEFAULT NULL,
    p_period_years integer DEFAULT NULL,
    p_levels text[] DEFAULT NULL,
    p_min_score integer DEFAULT NULL)
RETURNS TABLE (theme_key bigint, theme_name text, subject_area text, rank real, "position" bigint)
LANGUAGE sql STABLE PARALLEL SAFE AS
$$
    SELECT t.theme_key,
           t.theme_name,
           t.subject_area,
           NULL::real AS rank,
           row_number() OVER (ORDER BY coalesce(ts.judged, 0) DESC, t.theme_name) AS position
    FROM dw.dim_theme t
    JOIN dw.theme_summary s ON s.theme_sk = t.theme_sk
    LEFT JOIN dw.theme_strength ts ON ts.theme_sk = t.theme_sk
    WHERE s.judged_case_count > 0
      AND (p_min_score IS NULL OR ts.score >= p_min_score)
      AND ((p_courts IS NULL AND p_period_years IS NULL AND p_levels IS NULL)
           OR dw.theme_matches_filters(t.theme_sk, p_courts, p_period_years, p_levels))
    ORDER BY position
    LIMIT p_limit
$$;
