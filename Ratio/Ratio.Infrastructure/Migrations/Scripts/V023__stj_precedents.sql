CREATE TABLE dw.dim_precedent (
    precedent_sk bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    precedent_kind text NOT NULL,
    precedent_number integer NOT NULL,
    thesis text NOT NULL,
    question text,
    status text NOT NULL,
    judged_at date,
    judging_body text,
    source text NOT NULL DEFAULT 'stj_precedentes',
    source_url text NOT NULL,
    extracted_at timestamp with time zone NOT NULL,
    UNIQUE (precedent_kind, precedent_number)
);

CREATE TABLE dw.bridge_subject_precedent (
    subject_sk bigint NOT NULL REFERENCES dw.dim_subject (subject_sk),
    precedent_sk bigint NOT NULL REFERENCES dw.dim_precedent (precedent_sk) ON DELETE CASCADE,
    PRIMARY KEY (subject_sk, precedent_sk)
);

CREATE INDEX idx_bridge_subject_precedent_precedent ON dw.bridge_subject_precedent (precedent_sk);
