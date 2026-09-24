-- BuildNexus :: Construction Service — US-14 (Start, Complete & Hand Over Construction)
-- Which projects have had their final payment settled, so this service can answer
-- AC-4's handover precondition on its own.
--
-- Applied by DbUp at startup. Scripts run once, in filename order, and are
-- recorded in the schemaversions table — never edit one that has shipped, add
-- the next number instead.
--
-- Why a local marker rather than a call to the Payment Service
-- ------------------------------------------------------------
-- AC-4 gates handover on the final payment being settled. That fact belongs to the
-- Payment Service, and this service may not query its database — one schema per
-- service, no cross-service joins or foreign keys. A synchronous HTTP call would
-- also make handover fail whenever that service is down, for a fact that does not
-- change once it is true.
--
-- So the fact is replicated here off the payment-events topic, exactly as
-- milestone_setups replicates "the design was approved" off design-events (US-23).
-- The gate then becomes a SELECT in the same transaction as the write, and a
-- Payment Service outage delays nothing.
--
-- !!! CONTRACT NOT YET AGREED !!!
-- The Payment Service does not exist yet (services/payment-service holds only a
-- README), so the event this table is fed from — FinalPaymentSettled on
-- payment-events, carrying projectId and settledAt — is this story's proposal, not
-- a contract anyone has signed off. Named FinalPaymentSettled rather than
-- PaymentSettled deliberately: AC-4 is about the *final* payment, and a per-instalment
-- event of a similar name must not be mistaken for it. Until that service publishes
-- it, this table stays empty and handover is refused for every project — which is
-- the correct direction to fail, since the alternative would be handing projects
-- over unpaid.
--
-- This database is owned exclusively by the Construction Service. No other service
-- may query it or hold a foreign key into it — project_id is a plain column copied
-- off an event, never a key across a service boundary.

CREATE TABLE IF NOT EXISTS payment_settlements (
    -- The project, and the primary key: one settlement per project. Making it the
    -- key is what absorbs a redelivered event — Kafka delivers at least once, so a
    -- second copy must be a no-op rather than an error that wedges the consumer.
    project_id      CHAR(36) NOT NULL,
    -- The eventId of the envelope that created this row, for tracing a marker back
    -- to the message that caused it.
    source_event_id CHAR(36) NOT NULL,
    -- When the payment was settled, from the event's own payload — not when this
    -- service heard about it.
    settled_at      DATETIME(6) NOT NULL,
    -- When this service learned the fact. Kept for a human tracing a row back to
    -- the consumer run that wrote it.
    recorded_at     DATETIME(6) NOT NULL,
    CONSTRAINT pk_payment_settlements PRIMARY KEY (project_id)
) ENGINE = InnoDB;
