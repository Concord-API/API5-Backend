CREATE FUNCTION dw.search_themes(p_query text, p_limit integer DEFAULT 20)
RETURNS TABLE (theme_key bigint, theme_name text, subject_area text, rank real, "position" bigint)
LANGUAGE sql STABLE STRICT PARALLEL SAFE AS
$$
    WITH ranked AS (
        SELECT t.theme_key,
               t.theme_name,
               t.subject_area,
               greatest(
                   ts_rank(t.search_vector, websearch_to_tsquery('dw.pt_unaccent'::regconfig, dw.norm_pt(p_query))) * 10,
                   word_similarity(dw.norm_pt(p_query), t.theme_name_norm)
               ) AS rank
        FROM dw.dim_theme t
    )
    SELECT r.theme_key,
           r.theme_name,
           r.subject_area,
           r.rank,
           row_number() OVER (ORDER BY r.rank DESC, r.theme_name) AS position
    FROM ranked r
    WHERE r.rank >= 0.5
    ORDER BY position
    LIMIT p_limit
$$;
