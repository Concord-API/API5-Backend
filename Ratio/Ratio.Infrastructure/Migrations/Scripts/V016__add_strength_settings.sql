ALTER TABLE dw.strength_config
    ALTER COLUMN coverage_courts SET DEFAULT 5,
    ADD COLUMN min_judged_for_grade smallint NOT NULL DEFAULT 10,
    ADD COLUMN divergence_points smallint NOT NULL DEFAULT 20,
    ADD COLUMN min_judged_for_divergence smallint NOT NULL DEFAULT 5;
