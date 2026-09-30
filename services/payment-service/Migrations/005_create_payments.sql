-- BuildNexus :: Payment Service — US-16 (Record Payment)
-- The payments a Client records against an invoice. What reduces their
-- outstanding balance (AC-1) and, once it reaches zero, settles the invoice
-- (AC-2).
--
-- Applied by DbUp at startup. Scripts run once, in filename order, and are
-- recorded in the schemaversions table — never edit one that has shipped, add
-- the next number instead.
--
-- Why a table of payments rather than a running total on the invoice
-- ------------------------------------------------------------------
-- US-16 allows a payment that only partly covers the invoice, so a second
-- payment has to know what the first one left. A single `amount_paid` column on
-- invoices would hold the same number, but it would hold it as a figure somebody
-- has to remember to keep correct — and a bug there is silent, because nothing
-- else in the system could contradict it. Rows are the record; the outstanding
-- amount is derived from them (invoices.amount - SUM(payments.amount)), so the
-- two can never disagree. It also means "what did this client actually pay, and
-- when" is answerable, which a total is not.
--
-- This database is owned exclusively by the Payment Service. No other service may
-- query it or hold a foreign key into it.

CREATE TABLE IF NOT EXISTS payments (
    id          CHAR(36)       NOT NULL,
    -- The invoice being paid down.
    invoice_id  CHAR(36)       NOT NULL,
    -- What was paid. DECIMAL for the same reason as invoices.amount: money
    -- routed through binary floating point drifts, and this figure is summed —
    -- so a drift here compounds across every payment on the invoice and the
    -- outstanding amount stops adding up.
    amount      DECIMAL(15, 2) NOT NULL,
    -- The Client who recorded it, from the sub claim of their token. A payment
    -- with no payer is not an audit trail.
    paid_by     CHAR(36)       NOT NULL,
    recorded_at DATETIME(6)    NOT NULL,
    CONSTRAINT pk_payments PRIMARY KEY (id),
    -- A payment of zero moves nothing, and a negative one would silently raise
    -- the outstanding amount — a refund dressed as a payment. Refunds are not in
    -- this story, so the database refuses the shape outright.
    CONSTRAINT ck_payments_amount_positive CHECK (amount > 0),
    -- A key inside this service's own schema is fine, and wanted: a payment
    -- against an invoice that does not exist is not a thing. This is the one
    -- direction a key may point — never across a service boundary.
    CONSTRAINT fk_payments_invoice
        FOREIGN KEY (invoice_id) REFERENCES invoices (id) ON DELETE CASCADE,
    -- The read every recording does: this invoice's payments, to sum them.
    INDEX ix_payments_invoice (invoice_id, recorded_at)
) ENGINE = InnoDB;
