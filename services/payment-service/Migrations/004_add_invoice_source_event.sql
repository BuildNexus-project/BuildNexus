-- BuildNexus :: Payment Service — US-15 (Generate Quotation & Invoice)
-- Lets an invoice record the event that caused it, so the automatic path in
-- AC-2 ("or automatically off a ConstructionStarted or milestone event") cannot
-- bill a project twice.
--
-- Why a new migration instead of editing 003: DbUp records applied migrations by
-- filename in schemaversions, and any environment that already ran 003 has that
-- recorded and would never re-run an edited version. A separate ALTER runs on
-- top of what is there, so every environment reaches the same shape without a
-- database reset.
--
-- Why this column exists at all
-- -----------------------------
-- Kafka delivers at least once. Without a marker, a redelivered
-- ConstructionStarted — a consumer restart, a rebalance, an offset that failed
-- to commit — would raise a second invoice for the same project. Billing a
-- client twice is not a defect you can shrug off and clean up later, so the
-- guard is a uniqueness constraint in the database rather than a check in C#
-- that two consumer instances could both pass at once.
--
-- NULL for a manually generated invoice, which has no causing event. MySQL's
-- unique indexes permit repeated NULLs, so the constraint binds exactly the
-- rows it should: automatic invoices are one-per-event, and a Project Manager
-- can still raise as many manual invoices against a project as the job needs.

ALTER TABLE invoices
    ADD COLUMN source_event_id CHAR(36) NULL AFTER created_by,
    ADD CONSTRAINT uq_invoices_source_event UNIQUE (source_event_id);
