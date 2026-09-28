-- BuildNexus :: Payment Service — Final payment settlement
-- Which projects have finished construction, so this service can tell the
-- difference between "nothing is outstanding right now" and "nothing more will
-- ever be billed".
--
-- Why the distinction matters
-- ---------------------------
-- A project's outstanding balance reaching zero is NOT the same as its final
-- payment having landed. A Client who fully pays the invoice raised when their
-- build started owes nothing at that moment — and will owe more as later stages
-- are billed. Announcing FinalPaymentSettled then would let the Construction
-- Service hand the project over while most of it was still unbilled, which is a
-- worse failure than the one this fixes: handing over unpaid is exactly what
-- US-14's gate exists to prevent.
--
-- So the announcement waits for both facts, whichever arrives last:
--   * construction is complete  — no further invoices are expected
--   * the balance is zero       — nothing is owed on what was billed
--
-- The first of those belongs to the Construction Service, and this service may
-- not query its database. It is replicated here off ConstructionCompleted on
-- construction-events, which that service already publishes and this one
-- already subscribes to — the same arrangement project_owners uses, and no new
-- event type on either side.
--
-- Applied by DbUp at startup. Scripts run once, in filename order, and are
-- recorded in the schemaversions table — never edit one that has shipped, add
-- the next number instead.
--
-- This database is owned exclusively by the Payment Service. project_id is a
-- plain column copied off an event, never a key across a service boundary.

CREATE TABLE IF NOT EXISTS construction_completions (
    -- The project, and the primary key: a build completes once. Making it the
    -- key is what absorbs a redelivered ConstructionCompleted — Kafka delivers
    -- at least once, so a second copy must be a no-op rather than an error that
    -- wedges the consumer.
    project_id          CHAR(36)    NOT NULL,
    -- When the build was completed, from the event's own payload — not when
    -- this service heard about it.
    completed_at        DATETIME(6) NOT NULL,
    -- The eventId of the envelope that created this row, for tracing a marker
    -- back to the message that caused it.
    source_event_id     CHAR(36)    NOT NULL,
    -- When this service learned the fact.
    recorded_at         DATETIME(6) NOT NULL,
    -- When this service announced FinalPaymentSettled for the project, or NULL
    -- if it has not yet. This is what makes the announcement happen exactly
    -- once: both triggers — a settling payment and a completing build — check
    -- and set it inside their own transaction, so whichever arrives last
    -- announces and the other cannot announce again.
    announced_at        DATETIME(6) NULL,
    CONSTRAINT pk_construction_completions PRIMARY KEY (project_id)
) ENGINE = InnoDB;
