CREATE FUNCTION dw.norm_pt(txt text) RETURNS text
LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT AS
$$ SELECT lower(public.unaccent('public.unaccent'::regdictionary, txt)) $$;

ALTER TABLE dw.dim_theme
    ADD COLUMN search_vector tsvector GENERATED ALWAYS AS (to_tsvector('dw.pt_unaccent'::regconfig, theme_name)) STORED,
    ADD COLUMN theme_name_norm text GENERATED ALWAYS AS (dw.norm_pt(theme_name)) STORED;
