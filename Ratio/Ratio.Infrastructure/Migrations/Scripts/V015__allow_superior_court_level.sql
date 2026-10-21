ALTER TABLE dw.dim_case
    DROP CONSTRAINT dim_case_court_level_check;

ALTER TABLE dw.dim_case
    ADD CONSTRAINT dim_case_court_level_check
    CHECK (court_level IN ('First', 'Second', 'SpecialCourt', 'AppealPanel', 'Superior'));
