-- BuildNexus :: Project Service — US-08 (Cancel / Reject Project)
-- A terminal Cancelled status, and a column for the reason a change was made.
--
-- Applied by DbUp at startup. Scripts run once, in filename order, and are
-- recorded in the schemaversions table — never edit one that has shipped, add
-- the next number instead.

-- Cancelled is reachable from any status before Construction (US-08) and
-- nothing follows it. The three status constraints from 002 are replaced rather
-- than widened in place: MySQL has no ALTER CHECK.
ALTER TABLE projects
    DROP CHECK ck_projects_status;

ALTER TABLE projects
    ADD CONSTRAINT ck_projects_status
        CHECK (status IN ('Pending', 'Designing', 'DesignApproved', 'Construction', 'Completed', 'Cancelled'));

ALTER TABLE project_status_history
    DROP CHECK ck_project_status_history_to_status;

ALTER TABLE project_status_history
    ADD CONSTRAINT ck_project_status_history_to_status
        CHECK (to_status IN ('Pending', 'Designing', 'DesignApproved', 'Construction', 'Completed', 'Cancelled'));

ALTER TABLE project_status_history
    DROP CHECK ck_project_status_history_from_status;

ALTER TABLE project_status_history
    ADD CONSTRAINT ck_project_status_history_from_status
        CHECK (from_status IS NULL
               OR from_status IN ('Pending', 'Designing', 'DesignApproved', 'Construction', 'Completed', 'Cancelled'));

-- Why a change was made, when there is a reason worth keeping with the row.
-- NULL for every transition so far — a Pending -> Designing move speaks for
-- itself. US-08's cancellation is the first to record one: the reason the
-- Client or Admin gave for closing the project out.
ALTER TABLE project_status_history
    ADD COLUMN note TEXT NULL AFTER changed_by_role;
