-- BuildNexus :: Project Service — US-06 (View & Update Project Status)
-- The rest of a project's lifecycle, and the audit trail of how it got there.
--
-- Applied by DbUp at startup. Scripts run once, in filename order, and are
-- recorded in the schemaversions table — never edit one that has shipped, add
-- the next number instead. 001 is therefore left exactly as it was, and every
-- change it needs is made here as an ALTER.

-- US-05 constrained the column to the one status the system could produce.
-- US-06 defines the other four, so the constraint is replaced rather than
-- widened in place: MySQL has no ALTER CHECK.
ALTER TABLE projects
    DROP CHECK ck_projects_status;

ALTER TABLE projects
    ADD CONSTRAINT ck_projects_status
        CHECK (status IN ('Pending', 'Designing', 'DesignApproved', 'Construction', 'Completed'));

-- Who is working on the project. NULL until somebody is put on it, which is
-- every project today: no story has defined how staff are assigned yet, so
-- these are written by nothing and only read. They are here because US-06 says
-- the view shows the assigned architect and project manager, and because they
-- are what "assigned staff" in the access rule means.
--
-- Deliberately NOT foreign keys, for the same reason client_id is not: the
-- accounts live in the User Service's own database. Only the id is held, never
-- a copy of the person — a name shown here would go stale the moment they
-- changed it.
ALTER TABLE projects
    ADD COLUMN assigned_architect_id       CHAR(36) NULL AFTER status,
    ADD COLUMN assigned_project_manager_id CHAR(36) NULL AFTER assigned_architect_id;

-- One row per status the project has ever been in, so "how did this get here"
-- is answerable from the data rather than from logs that roll away.
--
-- Append-only by intent: nothing updates or deletes a row here. A correction is
-- a further transition, not an edit to the record of what actually happened.
CREATE TABLE IF NOT EXISTS project_status_history (
    id                 CHAR(36)     NOT NULL,
    project_id         CHAR(36)     NOT NULL,
    -- NULL on the opening row only — the project did not come from anywhere,
    -- it was created. Every later row names the status it left.
    from_status        VARCHAR(20)  NULL,
    to_status          VARCHAR(20)  NOT NULL,
    -- The user who caused the change, from the `sub` claim of their token, and
    -- the role they held while doing it. The role is recorded as it was at the
    -- time: an audit trail that re-reads today's role would rewrite history
    -- every time somebody is promoted.
    changed_by_user_id CHAR(36)     NOT NULL,
    changed_by_role    VARCHAR(20)  NOT NULL,
    changed_at         DATETIME     NOT NULL,
    CONSTRAINT pk_project_status_history PRIMARY KEY (id),
    -- A foreign key inside this service's own schema is fine, and wanted: the
    -- history of a project that does not exist is not a thing. This is the one
    -- direction a key may point — never across a service boundary.
    CONSTRAINT fk_project_status_history_project
        FOREIGN KEY (project_id) REFERENCES projects (id) ON DELETE CASCADE,
    CONSTRAINT ck_project_status_history_to_status
        CHECK (to_status IN ('Pending', 'Designing', 'DesignApproved', 'Construction', 'Completed')),
    CONSTRAINT ck_project_status_history_from_status
        CHECK (from_status IS NULL
               OR from_status IN ('Pending', 'Designing', 'DesignApproved', 'Construction', 'Completed')),
    -- The only way this table is ever read: one project's rows, oldest first.
    INDEX ix_project_status_history_project (project_id, changed_at)
) ENGINE = InnoDB;

-- Projects created before this table existed have no opening row, and a history
-- that starts halfway through is not the full record the story asks for. Their
-- creation is reconstructed from the row itself: it was Pending, it was the
-- client who submitted it, and it happened at created_at.
--
-- Guarded by NOT EXISTS rather than left to run once, so it stays correct if it
-- is ever replayed against a database that has already been through it.
INSERT INTO project_status_history
    (id, project_id, from_status, to_status, changed_by_user_id, changed_by_role, changed_at)
SELECT UUID(), p.id, NULL, p.status, p.client_id, 'Client', p.created_at
FROM projects p
WHERE NOT EXISTS (
    SELECT 1 FROM project_status_history h WHERE h.project_id = p.id
);
