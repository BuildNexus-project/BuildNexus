-- BuildNexus :: Project Service — US-26 (In-App Notifications from Events)
-- One row per person an event concerns: what happened, on which project, and whether they have
-- seen it yet.
--
-- Applied by DbUp at startup. Scripts run once, in filename order, and are recorded in the
-- schemaversions table — never edit one that has shipped, add the next number instead.
--
-- Why this lives in the Project Service
-- -------------------------------------
-- An event says which PROJECT it is about, never which people. Who is concerned is the project's
-- Client and its assigned Architect, and those two ids exist in exactly one place: the projects
-- table of this service. Keeping the notifications here means working out "who should be told"
-- is a lookup in our own schema, not a call to another service or a copy of its data.
--
-- user_id and project_id
-- ----------------------
-- user_id is NOT a foreign key: the account lives in the User Service's own database, and this
-- service holds no key across that boundary (the same as projects.client_id). project_id IS one —
-- inside this service's own schema a key is fine, and a notification about a project that does
-- not exist is not a thing.
--
-- The uniqueness rule
-- -------------------
-- (event_id, user_id) is unique. Kafka delivers at least once, so the same event arrives again
-- after a restart or a rebalance; the second pass hits this key and writes nothing, instead of
-- telling the person twice. event_id is the id from the event's envelope.
--
-- message is stored as finished text rather than rebuilt from the event on every read. The event
-- is not kept, and a notification should read the same next month as the day it arrived.

CREATE TABLE IF NOT EXISTS notifications (
    id          CHAR(36)     NOT NULL,
    user_id     CHAR(36)     NOT NULL,
    project_id  CHAR(36)     NOT NULL,
    event_id    CHAR(36)     NOT NULL,
    event_type  VARCHAR(30)  NOT NULL,
    message     VARCHAR(500) NOT NULL,
    -- When the event happened, off its envelope — not when this service got round to reading it.
    -- A consumer that was down for an hour must not make a payment look an hour newer.
    occurred_at DATETIME(6)  NOT NULL,
    -- NULL until the person has seen it. The time rather than a flag, so "when" is answerable.
    read_at     DATETIME(6)  NULL,
    CONSTRAINT pk_notifications PRIMARY KEY (id),
    CONSTRAINT fk_notifications_project
        FOREIGN KEY (project_id) REFERENCES projects (id) ON DELETE CASCADE,
    CONSTRAINT uq_notifications_event_user UNIQUE (event_id, user_id),
    -- The three events US-26 names, and nothing else. Held to the same list as
    -- NotificationEventTypes.All by a test, so adding a type is a deliberate change in both.
    CONSTRAINT ck_notifications_event_type
        CHECK (event_type IN ('DesignApproved', 'MilestoneCompleted', 'PaymentReceived')),
    -- The only way these are read: one person's rows, newest first.
    INDEX ix_notifications_user (user_id, occurred_at)
) ENGINE = InnoDB;
