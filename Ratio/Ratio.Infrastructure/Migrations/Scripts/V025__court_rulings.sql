CREATE TABLE dw.dim_ruling (
    ruling_sk bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    source text NOT NULL,
    source_id text NOT NULL,
    case_number text,
    court_code text NOT NULL,
    judging_body text,
    judged_at date,
    summary text NOT NULL,
    source_url text NOT NULL,
    extracted_at timestamp with time zone NOT NULL,
    UNIQUE (source, source_id)
);

CREATE TABLE dw.bridge_theme_ruling (
    theme_sk bigint NOT NULL REFERENCES dw.dim_theme (theme_sk) ON DELETE CASCADE,
    ruling_sk bigint NOT NULL REFERENCES dw.dim_ruling (ruling_sk) ON DELETE CASCADE,
    link_method text NOT NULL,
    similarity real,
    similarity_threshold real,
    PRIMARY KEY (theme_sk, ruling_sk),
    CONSTRAINT bridge_theme_ruling_similarity_check CHECK (similarity IS NULL OR similarity BETWEEN 0 AND 1),
    CONSTRAINT bridge_theme_ruling_threshold_check CHECK (similarity_threshold IS NULL OR similarity_threshold BETWEEN 0 AND 1)
);

CREATE INDEX idx_bridge_theme_ruling_ruling ON dw.bridge_theme_ruling (ruling_sk);
