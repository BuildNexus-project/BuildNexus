-- BuildNexus :: Construction Service — US-12 (Manage Construction Milestones)
-- One row per milestone a Project Manager defines for an approved project.
-- The design-approval gate (AC-1) is answered locally: a milestone may only be
-- created for a project that already has a row in milestone_setups, which the
-- DesignApproved Kafka consumer populates (US-23). No cross-service call is
-- needed, and this database owns every column below.
--
-- Applied by DbUp at startup. Scripts run once, in filename order, and are
-- recorded in the schemaversions table — never edit one that has shipped, add
-- the next number instead.
--
-- This database is owned exclusively by the Construction Service. No other
-- service may query it or hold a foreign key into it — the project id is a
-- plain column, copied off the milestone_setups row, never a key across a
-- service boundary.

CREATE TABLE IF NOT EXISTS construction_milestones (
    id             CHAR(36)     NOT NULL,
    -- The project this milestone belongs to. Not a foreign key into
    -- milestone_setups: the gate is enforced by the repository at insert time
    -- (a SELECT against milestone_setups in the same transaction), so an FK
    -- here would only duplicate a rule the code already carries — and would
    -- couple the two tables' lifetimes in a way the story does not ask for.
    project_id     CHAR(36)     NOT NULL,
    -- The Project Manager's own label for this milestone
    -- ("Foundation poured", "Roof on"). Unique per project so a table of them
    -- reads clearly and a repeated PATCH cannot accidentally address two.
    name           VARCHAR(150) NOT NULL,
    -- One of 'NotStarted', 'InProgress', 'Completed' — the three states AC-2
    -- names. Kept as VARCHAR rather than ENUM: enum values are painful to
    -- extend later, and the repository already refuses anything outside the
    -- three before the row reaches MySQL.
    status         VARCHAR(20)  NOT NULL DEFAULT 'NotStarted',
    -- DATETIME(6) — microsecond precision, not whole seconds. Every
    -- ListForProjectAsync query orders by created_at (with id as a
    -- defensive tiebreaker), and the PM sees the milestones in the order
    -- they were planned. Plain DATETIME would collapse three creates in
    -- the same wall-clock second to one value, and the Guid tiebreaker
    -- has no relationship to insertion order — the sibling
    -- milestone_setups table uses whole-second DATETIME because its
    -- tests only ever create one row per project per second, an
    -- assumption this table cannot make.
    created_at     DATETIME(6)  NOT NULL,
    updated_at     DATETIME(6)  NOT NULL,
    CONSTRAINT pk_construction_milestones PRIMARY KEY (id),
    -- Names are unique inside a project only — two projects may both have a
    -- "Foundation poured" milestone.
    CONSTRAINT uq_construction_milestones_project_name UNIQUE (project_id, name),
    -- Every read the story cares about is "the milestones for this project",
    -- so an index on project_id keeps the scan cheap once a project has many
    -- rows.
    INDEX ix_construction_milestones_project (project_id)
) ENGINE = InnoDB;