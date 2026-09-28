-- BuildNexus :: Payment Service — Final payment settlement
-- Lets this service announce that a project's final payment has landed, which
-- is the precondition US-14's handover has been waiting on since it shipped.
--
-- Why this migration exists
-- ------------------------
-- The Construction Service's handover gate reads a local replica of one fact
-- this service owns: that a project owes nothing more. It consumes
-- FinalPaymentSettled off payment-events to build that replica. Nothing has ever
-- published the event, so payment_settlements stays empty and handover is
-- refused for every project — correct, but it means a finished, fully paid
-- project can never be handed over without somebody putting a message on the
-- topic by hand.
--
-- Two changes are needed before that event can be written at all:
--
-- 1. invoice_id becomes nullable. A PaymentReceived is about one invoice, but a
--    FinalPaymentSettled is about the project as a whole — and it can be
--    triggered by construction completing rather than by any payment, in which
--    case there is no invoice to name. The foreign key stays: when there is an
--    invoice the row still points at a real one.
--
-- 2. The event-type CHECK widens to admit it. The constraint is the database
--    half of PaymentEventTypes and must list every type the service can raise.
--
-- Applied by DbUp at startup. Scripts run once, in filename order, and are
-- recorded in the schemaversions table — never edit one that has shipped, add
-- the next number instead.

ALTER TABLE payment_outbox_events
    MODIFY COLUMN invoice_id CHAR(36) NULL,
    DROP CHECK ck_payment_outbox_events_type,
    ADD CONSTRAINT ck_payment_outbox_events_type
        CHECK (event_type IN ('PaymentReceived', 'FinalPaymentSettled'));
