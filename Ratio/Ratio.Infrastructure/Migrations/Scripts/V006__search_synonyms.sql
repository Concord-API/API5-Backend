CREATE TABLE dw.search_synonym (
    term text PRIMARY KEY,
    expands_to text NOT NULL,
    note text
);

CREATE FUNCTION dw.expand_query(p_query text) RETURNS text
LANGUAGE sql STABLE PARALLEL SAFE AS
$$
    SELECT btrim(coalesce((
        SELECT string_agg(coalesce(s.expands_to, w.tok), ' ' ORDER BY w.ord)
        FROM unnest(regexp_split_to_array(dw.norm_pt(coalesce(p_query, '')), '\s+'))
             WITH ORDINALITY AS w(tok, ord)
        LEFT JOIN dw.search_synonym s ON s.term = w.tok
    ), ''))
$$;
