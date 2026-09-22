CREATE FUNCTION dw.norm_pt(txt text) RETURNS text
LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT AS
$$ SELECT lower(public.unaccent('public.unaccent'::regdictionary, txt)) $$;

ALTER TABLE dw.dim_theme
    ADD COLUMN search_vector tsvector GENERATED ALWAYS AS (to_tsvector('dw.pt_unaccent'::regconfig, theme_name)) STORED,
    ADD COLUMN theme_name_norm text GENERATED ALWAYS AS (dw.norm_pt(theme_name)) STORED;

CREATE INDEX idx_dim_theme_fts ON dw.dim_theme USING gin (search_vector);

CREATE INDEX idx_dim_theme_name_trgm ON dw.dim_theme USING gin (theme_name_norm gin_trgm_ops);
