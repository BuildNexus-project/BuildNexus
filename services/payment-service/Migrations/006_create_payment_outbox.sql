-- BuildNexus :: Payment Service — US-16 (Record Payment)
-- The events this service has raised and not yet put on payment-events (AC-3).
--
-- Applied by DbUp at startup. Scripts run once, in filename order, and are
-- recorded in the schemaversions table — never edit one that has shipped, add
-- the next number instead.
--
-- Why an outbox rather than publishing from the request path
-- ----------------------------------------------------------
-- AC-3 requires a PaymentReceived to be published when a payment is saved. A
-- publish made after the transaction commits can be lost — the process dies, the
-- broker is unreachable — leaving a payment recorded that nobody was told about,
-- and no way to notice. A publish made before it commits can announce a payment
-- that then rolls back. Writing the event here, inside the same transaction as
-- the payment, makes the two commit together or not at all; getting it onto Kafka
-- afterwards is the dispatcher's job, and a broker that is down delays that rather
-- than losing it. The same pattern the Design and Construction Services use.
--
-- This database is owned exclusively by the Payment Service.

CREATE TABLE IF NOT EXISTS payment_outbox_events (
    -- The dispatch order, and the primary key. AUTO_INCREMENT is monotonic by
    -- construction, so events drain in the order their transactions committed —
    -- which occurred_at cannot guarantee on its own.
    sequence_number BIGINT      NOT NULL AUTO_INCREMENT,
    -- The envelope's own eventId. Held as a column as well as inside the envelope
    -- so a message on the topic can be traced back to the row that produced it.
    id              CHAR(36)    NOT NULL,
    -- The project the event is about, and the Kafka message key: every event
    -- about one project lands on the same partition and so reaches consumers in
    -- the order it happened. Copied off the invoice inside the same transaction.
    --
    -- Deliberately NOT a foreign key. The only project table here is
    -- project_owners, which is a replica fed from project-events — a payment
    -- against a project whose ProjectCreated has not been consumed yet is
    -- perfectly legitimate, and a key would refuse to record the event for it.
    project_id      CHAR(36)    NOT NULL,
    -- The invoice the payment was against, so an event can be traced to it
    -- without parsing the envelope.
    invoice_id      CHAR(36)    NOT NULL,
    event_type      VARCHAR(50) NOT NULL,
    -- The complete envelope as it will go on the wire, serialised at the moment
    -- the payment was recorded rather than at dispatch. LONGTEXT and not MySQL's
    -- JSON type, deliberately: a JSON column normalises what it stores and hands
    -- back its keys in its own order, so a retry would put different bytes on the
    -- topic than the first attempt did — and eventId and occurredAt must describe
    -- when the payment happened, not when we last managed to send it.
    envelope        LONGTEXT    NOT NULL,
    occurred_at     DATETIME(6) NOT NULL,
    -- NULL while the event is still waiting. Set once the broker has acknowledged
    -- it, which is what makes this row's work done.
    published_at    DATETIME(6) NULL,
    -- How many times dispatch has been tried, and what went wrong last time. An
    -- event stuck at a rising attempt count is the signal that something needs a
    -- human, which is why the attempt is written down rather than only retried.
    attempt_count   INT         NOT NULL DEFAULT 0,
    last_error      TEXT        NULL,
    CONSTRAINT pk_payment_outbox_events PRIMARY KEY (sequence_number),
    -- One row per published event. If a retry ever tried to write a second row
    -- for the same envelope, this refuses it rather than duplicating the event.
    CONSTRAINT uq_payment_outbox_events_id UNIQUE (id),
    -- A key inside this service's own schema, and wanted: a payment event about
    -- an invoice that does not exist is not a thing.
    CONSTRAINT fk_payment_outbox_events_invoice
        FOREIGN KEY (invoice_id) REFERENCES invoices (id) ON DELETE CASCADE,
    -- The one event US-16 names, and no more. An event type outside this list is
    -- one no consumer agreed to; the constraint is here so an invented one fails
    -- at the write rather than on somebody else's topic.
    CONSTRAINT ck_payment_outbox_events_type CHECK (event_type IN ('PaymentReceived')),
    CONSTRAINT ck_payment_outbox_events_attempts CHECK (attempt_count >= 0),
    -- The dispatcher's only query: the unsent rows, oldest first. published_at
    -- leads, so the pending rows sit together at the head of the index and stay
    -- cheap to find however many delivered ones accumulate behind them.
    INDEX ix_payment_outbox_events_pending (published_at, sequence_number),
    -- And one invoice's events, in the order they were raised.
    INDEX ix_payment_outbox_events_invoice (invoice_id, sequence_number)
) ENGINE = InnoDB;
