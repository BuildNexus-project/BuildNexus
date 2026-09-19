-- BuildNexus :: Design Service — US-11 (Review Design: Approve or Request Revision)
-- The transactional outbox: events waiting to reach the message bus.
--
-- Applied by DbUp at startup. Scripts run once, in filename order, and are
-- recorded in the schemaversions table — never edit one that has shipped, add
-- the next number instead.
--
-- Same pattern as project_outbox_events in the Project Service, and the same
-- reason: the event is written here in the same transaction as the state
-- change it describes, so it commits with the change or not at all, and a
-- background dispatcher drains it to Kafka afterwards. The broker being down
-- then delays delivery instead of losing it.
--
-- document_id, not project_id: design_documents is this service's own local
-- aggregate root, in this same schema, so it is what the foreign key and the
-- Kafka partition key are built on — the same reasoning project_outbox_events
-- applies to projects, applied to what this service actually owns. A project
-- id still travels inside the envelope's payload for a consumer that needs it,
-- exactly as design_documents.project_id already does.
CREATE TABLE IF NOT EXISTS design_outbox_events (
    -- The dispatch order, and the primary key. AUTO_INCREMENT is monotonic by
    -- construction, so events drain in the order their transactions committed.
    sequence_number BIGINT       NOT NULL AUTO_INCREMENT,
    -- The envelope's own eventId. Held as a column as well as inside the
    -- envelope so a redelivery can be traced back to the row that caused it.
    id              CHAR(36)     NOT NULL,
    -- The aggregate the event is about, and the Kafka message key: every event
    -- about one document lands on the same partition and reaches consumers in
    -- the order it happened.
    document_id     CHAR(36)     NOT NULL,
    event_type      VARCHAR(50)  NOT NULL,
    -- The complete envelope as it will go on the wire, serialised at the moment
    -- of the state change rather than at dispatch. LONGTEXT, not MySQL's JSON
    -- type — see project_outbox_events.envelope for why: a retry must put the
    -- same bytes on the topic as the first attempt did.
    envelope        LONGTEXT     NOT NULL,
    occurred_at     DATETIME     NOT NULL,
    -- NULL while the event is still waiting. Set once the broker has
    -- acknowledged it, which is what makes this row's work done.
    published_at    DATETIME     NULL,
    -- How many times dispatch has been tried, and what went wrong last time.
    attempt_count   INT          NOT NULL DEFAULT 0,
    last_error      TEXT         NULL,
    CONSTRAINT pk_design_outbox_events PRIMARY KEY (sequence_number),
    -- One row per published event. If a retry ever tried to write a second row
    -- for the same envelope, this refuses it rather than duplicating the event.
    CONSTRAINT uq_design_outbox_events_id UNIQUE (id),
    -- A foreign key inside this service's own schema is fine, and wanted: an
    -- event about a document that does not exist is not a thing. This is the
    -- one direction a key may point — never across a service boundary, which is
    -- exactly why this is document_id and not project_id.
    CONSTRAINT fk_design_outbox_events_document
        FOREIGN KEY (document_id) REFERENCES design_documents (id) ON DELETE CASCADE,
    -- The one event US-11 names. An event type outside this list is one no
    -- consumer agreed to; the constraint is here so an invented one fails at
    -- the write rather than on somebody else's topic.
    CONSTRAINT ck_design_outbox_events_type
        CHECK (event_type IN ('DesignApproved')),
    CONSTRAINT ck_design_outbox_events_attempts CHECK (attempt_count >= 0),
    -- The dispatcher's only query: the unsent rows, oldest first.
    INDEX ix_design_outbox_events_pending (published_at, sequence_number),
    -- And the read a future "events for this document" endpoint would serve.
    INDEX ix_design_outbox_events_document (document_id, sequence_number)
) ENGINE = InnoDB;
