CREATE TABLE dw.theme_narrative (
    theme_sk bigint PRIMARY KEY REFERENCES dw.dim_theme (theme_sk),
    lead jsonb NOT NULL,
    body jsonb NOT NULL,
    text_origin text NOT NULL,
    methodology_version text NOT NULL,
    generated_at date NOT NULL,
    CONSTRAINT theme_narrative_text_origin_check CHECK (text_origin IN ('template', 'curated'))
);
