-- BuildNexus :: Project Service — US-22 (Project Event Integration)
-- The transactional outbox: events waiting to reach the message bus.
--
-- Applied by DbUp at startup. Scripts run once, in filename order, and are
-- recorded in the schemaversions table — never edit one that has shipped, add
-- the next number instead.
--
-- Why this exists
-- ---------------
-- US-05 published ProjectCreated straight to Kafka from inside the HTTP
-- request, and swallowed the failure: the project was already committed, so
-- answering with a 500 would have told the Client their submission was lost
-- when it was not. That trade left a real gap, and US-22's first acceptance
-- bullet — events publish *reliably* — is the story that closes it. A state
-- change made while the broker was unreachable was simply never announced, and
-- nothing recorded that it had not been.
--
-- The fix is to stop treating "the row is saved" and "the event is sent" as two
-- separate things that might disagree. The event is written here in the same
-- transaction as the state change it describes, so it commits with the change
-- or not at all, and a background dispatcher drains it to Kafka afterwards. The
-- broker being down then delays delivery instead of losing it.

CREATE TABLE IF NOT EXISTS project_outbox_events (
    -- The dispatch order, and the primary key. AUTO_INCREMENT is monotonic by
    -- construction, so events drain in the order their transactions committed
    -- — the same lesson 003 learned about the status history, applied before it
    -- can bite: occurred_at is a whole-second DATETIME, and two events about
    -- one project in the same second must not be sent in an order decided by a
    -- random GUID. ProjectUpdated and ProjectApproved are written together in
    -- one transaction, so this is the common case here, not the rare one.
    sequence_number BIGINT       NOT NULL AUTO_INCREMENT,
    -- The envelope's own eventId. Held as a column as well as inside the
    -- envelope so a redelivery can be traced back to the row that caused it.
    id              CHAR(36)     NOT NULL,
    -- The aggregate the event is about, and the Kafka message key: every event
    -- about one project lands on the same partition and reaches consumers in
    -- the order it happened.
    project_id      CHAR(36)     NOT NULL,
    event_type      VARCHAR(50)  NOT NULL,
    -- The complete envelope as it will go on the wire, serialised at the moment
    -- of the state change rather than at dispatch.
    --
    -- LONGTEXT and not MySQL's JSON type, deliberately: a JSON column stores a
    -- normalised form and hands back its keys in its own order, so a retry
    -- would put different bytes on the topic than the first attempt did. The
    -- envelope's four properties are an agreed shape, and eventId and
    -- occurredAt must describe when the thing *happened*, not when we last
    -- managed to send it — otherwise a consumer deduplicating on eventId would
    -- treat every retry as a new event.
    envelope        LONGTEXT     NOT NULL,
    occurred_at     DATETIME     NOT NULL,
    -- NULL while the event is still waiting. Set once the broker has
    -- acknowledged it, which is what makes this row's work done.
    published_at    DATETIME     NULL,
    -- How many times dispatch has been tried, and what went wrong last time.
    -- An event stuck at a rising attempt count is the signal that something
    -- needs a human, which is the whole point of writing the attempt down
    -- rather than retrying silently.
    attempt_count   INT          NOT NULL DEFAULT 0,
    last_error      TEXT         NULL,
    CONSTRAINT pk_project_outbox_events PRIMARY KEY (sequence_number),
    -- One row per published event. If a retry ever tried to write a second row
    -- for the same envelope, this refuses it rather than duplicating the event.
    CONSTRAINT uq_project_outbox_events_id UNIQUE (id),
    -- A foreign key inside this service's own schema is fine, and wanted: an
    -- event about a project that does not exist is not a thing. This is the one
    -- direction a key may point — never across a service boundary.
    CONSTRAINT fk_project_outbox_events_project
        FOREIGN KEY (project_id) REFERENCES projects (id) ON DELETE CASCADE,
    -- The three events US-22 names. An event type outside this list is one no
    -- consumer agreed to; the constraint is here so an invented one fails at
    -- the write rather than on somebody else's topic.
    CONSTRAINT ck_project_outbox_events_type
        CHECK (event_type IN ('ProjectCreated', 'ProjectUpdated', 'ProjectApproved')),
    CONSTRAINT ck_project_outbox_events_attempts CHECK (attempt_count >= 0),
    -- The dispatcher's only query: the unsent rows, oldest first. published_at
    -- leads, so the pending rows sit together at the head of the index and stay
    -- cheap to find however many delivered ones accumulate behind them.
    INDEX ix_project_outbox_events_pending (published_at, sequence_number),
    -- And the read the events endpoint serves: one project's events, in the
    -- order they were raised.
    INDEX ix_project_outbox_events_project (project_id, sequence_number)
) ENGINE = InnoDB;
