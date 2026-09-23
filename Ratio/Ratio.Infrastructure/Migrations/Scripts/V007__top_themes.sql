CREATE FUNCTION dw.top_themes(p_limit integer DEFAULT 20)
RETURNS TABLE (theme_key bigint, theme_name text, subject_area text, rank real, "position" bigint)
LANGUAGE sql STABLE STRICT PARALLEL SAFE AS
$$
    SELECT t.theme_key,
           t.theme_name,
           t.subject_area,
           NULL::real AS rank,
           row_number() OVER (ORDER BY coalesce(ts.judged, 0) DESC, t.theme_name) AS position
    FROM dw.dim_theme t
    LEFT JOIN dw.theme_strength ts ON ts.theme_sk = t.theme_sk
    ORDER BY position
    LIMIT p_limit
$$;
