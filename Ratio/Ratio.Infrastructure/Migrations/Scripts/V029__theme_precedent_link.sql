CREATE TABLE dw.bridge_theme_precedent (
    theme_sk bigint NOT NULL REFERENCES dw.dim_theme (theme_sk) ON DELETE CASCADE,
    precedent_sk bigint NOT NULL REFERENCES dw.dim_precedent (precedent_sk) ON DELETE CASCADE,
    link_method text NOT NULL,
    similarity real NOT NULL CHECK (similarity >= 0 AND similarity <= 1),
    embedding_model text NOT NULL,
    PRIMARY KEY (theme_sk, precedent_sk)
);

CREATE INDEX idx_bridge_theme_precedent_precedent ON dw.bridge_theme_precedent (precedent_sk);
