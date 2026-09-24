-- BuildNexus :: Construction Service — US-14 (Start, Complete & Hand Over Construction)
-- One row per project whose build phase has formally begun, carrying where that
-- phase stands: Started, Completed, or HandedOver.
--
-- Applied by DbUp at startup. Scripts run once, in filename order, and are
-- recorded in the schemaversions table — never edit one that has shipped, add
-- the next number instead.
--
-- Why a row per project rather than a column on something existing
-- ---------------------------------------------------------------
-- US-12 and US-13 gave this service two facts about a project: whether its
-- design was approved (a milestone_setups row) and what its milestones say (the
-- construction_milestones rows). Neither records that a human decided to begin
-- the build — "every milestone is NotStarted" is indistinguishable from "nobody
-- has pressed start". US-14's first two Acceptance Criteria are explicitly about
-- that decision being recorded, so it gets its own row, and the absence of a row
-- is what "not started" means.
--
-- The three transitions are gated independently (AC-3), and each gate reads
-- state that already exists rather than copying it:
--   start     — milestone_setups row present, and at least one milestone defined
--   complete  — this row is 'Started', and every milestone is 'Completed'
--   handover  — this row is 'Completed' (plus the final-payment precondition)
--
-- This database is owned exclusively by the Construction Service. No other
-- service may query it or hold a foreign key into it — project_id is a plain
-- column, never a key across a service boundary.

CREATE TABLE IF NOT EXISTS construction_phases (
    -- The project, and the primary key: a project has one build phase, and its
    -- absence is the "construction has not started" state. Making it the key is
    -- also what makes the start transition race-safe — two simultaneous starts
    -- cannot both insert, so the second is refused by the engine rather than by
    -- a check that read before the other wrote.
    project_id     CHAR(36)    NOT NULL,
    -- One of 'Started', 'Completed', 'HandedOver'. VARCHAR rather than ENUM for
    -- the same reason construction_milestones.status is: enum values are painful
    -- to extend, and the repository refuses anything outside the three before a
    -- row reaches MySQL.
    status         VARCHAR(20) NOT NULL,
    -- When the Project Manager started the build. Never null: the row only
    -- exists because construction started.
    started_at     DATETIME(6) NOT NULL,
    -- Set when construction was marked complete, null until then. The null is
    -- load-bearing alongside status — a human reading the table can see when
    -- each stage happened, not only which stage is current.
    completed_at   DATETIME(6) NULL,
    -- Set when the finished project was handed over to the Client, null until
    -- then. Once set, the project is in its terminal state.
    handed_over_at DATETIME(6) NULL,
    -- Stamped on every transition, so a caller can see when the phase last moved.
    updated_at     DATETIME(6) NOT NULL,
    CONSTRAINT pk_construction_phases PRIMARY KEY (project_id)
) ENGINE = InnoDB;
