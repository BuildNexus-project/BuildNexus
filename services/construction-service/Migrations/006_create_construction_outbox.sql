-- BuildNexus :: Construction Service — US-14 (Start, Complete & Hand Over Construction)
-- The transactional outbox: construction events waiting to reach the message bus.
--
-- Applied by DbUp at startup. Scripts run once, in filename order, and are
-- recorded in the schemaversions table — never edit one that has shipped, add
-- the next number instead.
--
-- Why this exists
-- ---------------
-- US-14 is the first story in which this service publishes anything; until now it
-- only consumed. AC-1 and AC-2 require an event to go out when a transition is
-- made, and publishing straight to Kafka from inside the HTTP request would make
-- that a promise this service cannot keep: the phase row is already committed by
-- then, so a broker that is unreachable would leave the build started and the
-- Project Service never told — with nothing written down to say so.
--
-- So the event is written here in the same transaction as the transition it
-- describes. It commits with the transition or not at all, and a background
-- dispatcher drains it to Kafka afterwards. The broker being down then delays
-- delivery instead of losing it. Same pattern, and the same reasoning, as
-- project_outbox_events and design_outbox_events.

CREATE TABLE IF NOT EXISTS construction_outbox_events (
    -- The dispatch order, and the primary key. AUTO_INCREMENT is monotonic by
    -- construction, so events drain in the order their transactions committed —
    -- which occurred_at cannot guarantee on its own.
    sequence_number BIGINT      NOT NULL AUTO_INCREMENT,
    -- The envelope's own eventId. Held as a column as well as inside the
    -- envelope so a message on the topic can be traced back to the row that
    -- produced it.
    id              CHAR(36)    NOT NULL,
    -- The project the event is about, and the Kafka message key: every event
    -- about one project lands on the same partition and so reaches consumers in
    -- the order it happened.
    project_id      CHAR(36)    NOT NULL,
    event_type      VARCHAR(50) NOT NULL,
    -- The complete envelope as it will go on the wire, serialised at the moment
    -- of the transition rather than at dispatch. LONGTEXT and not MySQL's JSON
    -- type, deliberately: a JSON column normalises what it stores and hands back
    -- its keys in its own order, so a retry would put different bytes on the
    -- topic than the first attempt did — and eventId and occurredAt must
    -- describe when the transition happened, not when we last managed to send it.
    envelope        LONGTEXT    NOT NULL,
    occurred_at     DATETIME(6) NOT NULL,
    -- NULL while the event is still waiting. Set once the broker has
    -- acknowledged it, which is what makes this row's work done.
    published_at    DATETIME(6) NULL,
    -- How many times dispatch has been tried, and what went wrong last time. An
    -- event stuck at a rising attempt count is the signal that something needs a
    -- human, which is why the attempt is written down rather than only retried.
    attempt_count   INT         NOT NULL DEFAULT 0,
    last_error      TEXT        NULL,
    CONSTRAINT pk_construction_outbox_events PRIMARY KEY (sequence_number),
    -- One row per published event. If a retry ever tried to write a second row
    -- for the same envelope, this refuses it rather than duplicating the event.
    CONSTRAINT uq_construction_outbox_events_id UNIQUE (id),
    -- A foreign key inside this service's own schema is fine, and wanted: a
    -- construction event about a project whose build never started is not a
    -- thing. This is the one direction a key may point — never across a service
    -- boundary.
    CONSTRAINT fk_construction_outbox_events_phase
        FOREIGN KEY (project_id) REFERENCES construction_phases (project_id) ON DELETE CASCADE,
    -- The two events US-14 names, and no more. An event type outside this list is
    -- one no consumer agreed to; the constraint is here so an invented one fails
    -- at the write rather than on somebody else's topic.
    CONSTRAINT ck_construction_outbox_events_type
        CHECK (event_type IN ('ConstructionStarted', 'ConstructionCompleted')),
    CONSTRAINT ck_construction_outbox_events_attempts CHECK (attempt_count >= 0),
    -- The dispatcher's only query: the unsent rows, oldest first. published_at
    -- leads, so the pending rows sit together at the head of the index and stay
    -- cheap to find however many delivered ones accumulate behind them.
    INDEX ix_construction_outbox_events_pending (published_at, sequence_number),
    -- And one project's events, in the order they were raised.
    INDEX ix_construction_outbox_events_project (project_id, sequence_number)
) ENGINE = InnoDB;
