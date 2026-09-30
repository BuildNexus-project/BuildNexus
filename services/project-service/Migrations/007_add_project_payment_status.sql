-- BuildNexus :: Project Service — US-24 (Construction & Payment Event Integration)
-- Where a project stands financially, reflected from the Payment Service's PaymentReceived
-- events.
--
-- Applied by DbUp at startup. Scripts run once, in filename order, and are recorded in the
-- schemaversions table — never edit one that has shipped, add the next number instead.
--
-- Why this lives here at all
-- -------------------------
-- US-24's fourth scenario: when Payment Service announces a payment, this project's payment
-- status is updated accordingly. The money itself belongs to Payment Service and stays there
-- — this is a reflection of one fact off the topic, the same arrangement by which this
-- service already learns nothing about invoices and Construction Service learns nothing about
-- project status except through events.
--
-- What the three values mean, and what they deliberately do not
-- ------------------------------------------------------------
--   NotInvoiced    no PaymentReceived has been seen for this project. The default for every
--                  existing row and every new one.
--   PartiallyPaid  a payment arrived and the invoice it was against is still Pending.
--   InvoicePaid    a payment arrived and settled the invoice it was against.
--
-- InvoicePaid means "the invoice that payment was against is now settled". It does NOT mean
-- the project owes nothing: a PaymentReceived event carries one invoice's status, and this
-- service does not know how many other invoices the project has — nor may it ask, since that
-- is Payment Service's schema. A truthful project-level "FullyPaid" would have to be computed
-- by Payment Service, which is the only service that can see the whole ledger, and published
-- as its own determination. That is not an event US-24 names, so it is not invented here.
--
-- last_payment_event_id is the idempotency key. Kafka delivers at least once, so the same
-- PaymentReceived will arrive again; comparing against the last event applied makes the second
-- pass a no-op instead of a second write with a newer timestamp.

ALTER TABLE projects
    ADD COLUMN payment_status VARCHAR(20) NOT NULL DEFAULT 'NotInvoiced' AFTER status,
    ADD COLUMN payment_status_updated_at DATETIME(6) NULL AFTER payment_status,
    ADD COLUMN last_payment_event_id CHAR(36) NULL AFTER payment_status_updated_at,
    ADD CONSTRAINT ck_projects_payment_status
        CHECK (payment_status IN ('NotInvoiced', 'PartiallyPaid', 'InvoicePaid'));
