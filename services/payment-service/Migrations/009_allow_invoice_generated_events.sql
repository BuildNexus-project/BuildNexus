-- BuildNexus :: Payment Service — US-24 (Construction & Payment Event Integration)
-- Let the outbox carry InvoiceGenerated alongside the two types already allowed.
--
-- Applied by DbUp at startup. Scripts run once, in filename order, and are recorded in
-- the schemaversions table — never edit one that has shipped, add the next number instead.
--
-- Numbered 009, not 007
-- ---------------------
-- This script was first written as 007, before the FinalPaymentSettled work was brought
-- onto this branch and took 007 and 008. Two files numbered 007 would not have collided in
-- Git — they are different filenames — but they would have collided in the database, and
-- silently: DbUp applies scripts in filename order, "007_allow_final..." sorts before
-- "007_allow_invoice...", so this one would have run second and re-added the constraint
-- listing only PaymentReceived and InvoiceGenerated. FinalPaymentSettled would have
-- vanished from the allowed set, and Payment Service would have become unable to write the
-- event US-14's handover gate waits for — with no test on either branch able to notice,
-- since neither had both migrations.
--
-- Hence the full list below rather than just the new member: a DROP-and-ADD of a shared
-- constraint must restate everything that is still permitted, not only what it is adding.

ALTER TABLE payment_outbox_events
    DROP CHECK ck_payment_outbox_events_type;

ALTER TABLE payment_outbox_events
    ADD CONSTRAINT ck_payment_outbox_events_type
        CHECK (event_type IN ('PaymentReceived', 'FinalPaymentSettled', 'InvoiceGenerated'));
