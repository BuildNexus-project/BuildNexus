-- BuildNexus :: Construction Service — US-23 (Design Approval Event Integration)
-- The placeholder a DesignApproved event creates: a marker that a project's
-- construction prep should be set up. The real milestone data arrives with a
-- later story; this is only the trigger point.
--
-- Applied by DbUp at startup. Scripts run once, in filename order, and are
-- recorded in the schemaversions table — never edit one that has shipped, add
-- the next number instead.
--
-- This database is owned exclusively by the Construction Service. No other
-- service may query it or hold a foreign key into it — the project id is a
-- plain column, copied off the event, never a key across a service boundary.

CREATE TABLE IF NOT EXISTS milestone_setups (
    id                 CHAR(36)     NOT NULL,
    -- The project whose design was approved. One placeholder per project: the
    -- first DesignApproved for a project creates it, and every later one for
    -- the same project is a no-op — construction prep is a project-level thing,
    -- and Kafka's at-least-once delivery means the consumer has to be
    -- idempotent regardless.
    project_id         CHAR(36)     NOT NULL,
    -- The design document whose approval triggered this. Kept for a human
    -- tracing a placeholder back to its cause; not unique, since a project's
    -- second approved document does not create a second row.
    source_document_id CHAR(36)     NOT NULL,
    -- The eventId of the DesignApproved envelope that created it — a redelivery
    -- of the same event lands on the same project_id and is absorbed by the
    -- unique index below.
    source_event_id    CHAR(36)     NOT NULL,
    -- When the design was approved, from the event's occurredAt.
    approved_at        DATETIME     NOT NULL,
    created_at         DATETIME     NOT NULL,
    CONSTRAINT pk_milestone_setups PRIMARY KEY (id),
    -- One per project. INSERT ... the consumer relies on this to make a
    -- repeated DesignApproved a no-op rather than a duplicate.
    CONSTRAINT uq_milestone_setups_project UNIQUE (project_id)
) ENGINE = InnoDB;
