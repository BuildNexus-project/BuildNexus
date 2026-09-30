-- BuildNexus :: Payment Service — US-15 (Generate Quotation & Invoice)
-- What has actually been billed against a project, as opposed to what it was
-- estimated to cost (AC-2).
--
-- Applied by DbUp at startup. Scripts run once, in filename order, and are
-- recorded in the schemaversions table — never edit one that has shipped, add
-- the next number instead.
--
-- This database is owned exclusively by the Payment Service. No other service
-- may query it or hold a foreign key into it — project_id is a plain column,
-- never a key across a service boundary. In particular there is no key into
-- quotations: an invoice is not a child of an estimate. A project can be
-- invoiced without ever having been quoted, and re-quoting must not disturb
-- what has already been billed.

CREATE TABLE IF NOT EXISTS invoices (
    -- AC-2's "unique ID". A UUID rather than a running number: invoices are
    -- raised from two places — a person, and the construction-events consumer —
    -- and a sequence shared between them would need a lock held across the
    -- Kafka handler to stay gap-free.
    id         CHAR(36)       NOT NULL,
    -- The project being billed.
    project_id CHAR(36)       NOT NULL,
    -- AC-2's "an amount". DECIMAL for the same reason as quotations.estimated_total:
    -- money routed through binary floating point drifts, and an invoice that
    -- renders a cent off is one nobody trusts.
    amount     DECIMAL(15, 2) NOT NULL,
    -- AC-2's "a status of Pending or Paid". Stored as the name rather than an
    -- integer so a DBA reading the table can see what a row means, and
    -- constrained so a third value cannot be written by mistake — the CHECK is
    -- the database half of the InvoiceStatus enum.
    status     VARCHAR(20)    NOT NULL,
    -- Who raised it. For a manually generated invoice that is the Project
    -- Manager or Admin who asked for it; for one raised automatically off a
    -- ConstructionStarted event it is the Project Manager whose decision to
    -- start the build triggered it. Either way a real person is answerable for
    -- the bill, which is why this is NOT NULL.
    created_by CHAR(36)       NOT NULL,
    created_at DATETIME(6)    NOT NULL,
    -- When the invoice was settled, or NULL while it is still Pending. Kept
    -- alongside the status rather than derived from it: "Paid" without a date is
    -- half an answer to "when did this project pay?".
    paid_at    DATETIME(6)    NULL,
    CONSTRAINT pk_invoices PRIMARY KEY (id),
    CONSTRAINT ck_invoices_amount_positive CHECK (amount > 0),
    CONSTRAINT ck_invoices_status CHECK (status IN ('Pending', 'Paid')),
    -- A Paid invoice has a settlement date and a Pending one does not. Enforced
    -- here so the two columns cannot drift into saying different things.
    CONSTRAINT ck_invoices_paid_at_matches_status CHECK (
        (status = 'Paid' AND paid_at IS NOT NULL)
        OR (status = 'Pending' AND paid_at IS NULL)
    ),
    -- Serves the reads this story needs — a project's invoices, newest first.
    INDEX ix_invoices_project_created (project_id, created_at DESC)
) ENGINE = InnoDB;
