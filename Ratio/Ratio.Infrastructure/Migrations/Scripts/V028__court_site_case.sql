CREATE TABLE dw.case_site_detail (
    case_sk bigint PRIMARY KEY REFERENCES dw.dim_case (case_sk),
    status text NOT NULL CHECK (status IN ('found', 'not_found', 'sealed')),
    case_class text,
    subjects text[] NOT NULL DEFAULT '{}',
    judging_body text,
    judge text,
    filed_at date,
    decision_date date,
    decision_result text,
    situation text,
    source text NOT NULL,
    source_url text NOT NULL,
    extracted_at timestamp with time zone NOT NULL
);

CREATE TABLE dw.case_site_movement (
    case_sk bigint NOT NULL REFERENCES dw.case_site_detail (case_sk) ON DELETE CASCADE,
    seq integer NOT NULL,
    moved_at date NOT NULL,
    description text NOT NULL,
    PRIMARY KEY (case_sk, seq)
);
