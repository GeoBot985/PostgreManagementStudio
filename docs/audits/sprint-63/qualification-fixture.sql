\set ON_ERROR_STOP on

DROP SCHEMA IF EXISTS qualification CASCADE;
CREATE SCHEMA qualification AUTHORIZATION pms_s63;
SET search_path = qualification, public;

CREATE TYPE qualification.mood AS ENUM ('calm', 'busy', 'focused');
CREATE DOMAIN qualification.positive_amount AS numeric(12,2)
    CHECK (VALUE >= 0);

CREATE SEQUENCE qualification.reference_number_seq START 1000;

CREATE TABLE qualification.customer (
    customer_id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    external_id uuid NOT NULL UNIQUE,
    display_name text NOT NULL,
    created_at timestamp with time zone NOT NULL DEFAULT now()
);

CREATE TABLE qualification.example_table (
    id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    customer_id integer NOT NULL REFERENCES qualification.customer(customer_id),
    code text NOT NULL UNIQUE,
    payload jsonb NOT NULL,
    tags text[] NOT NULL,
    mood qualification.mood NOT NULL,
    amount qualification.positive_amount NOT NULL,
    occurred_at timestamp with time zone NOT NULL,
    local_stamp timestamp without time zone NOT NULL,
    notes text,
    empty_text text NOT NULL DEFAULT '',
    generated_code text GENERATED ALWAYS AS (upper(code)) STORED
);

COMMENT ON TABLE qualification.example_table IS
    'Sprint 63 release qualification table';
COMMENT ON COLUMN qualification.example_table.payload IS
    'Structured JSONB qualification payload';

CREATE INDEX ix_example_table_customer
    ON qualification.example_table(customer_id);

CREATE TABLE qualification.audit_log (
    audit_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    example_id integer,
    action text NOT NULL,
    changed_at timestamp with time zone NOT NULL DEFAULT now()
);

CREATE FUNCTION qualification.capture_example_insert()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    INSERT INTO qualification.audit_log(example_id, action)
    VALUES (NEW.id, 'insert');
    RETURN NEW;
END
$$;

CREATE TRIGGER tr_example_insert
AFTER INSERT ON qualification.example_table
FOR EACH ROW EXECUTE FUNCTION qualification.capture_example_insert();

CREATE FUNCTION qualification.example_count()
RETURNS bigint
LANGUAGE sql
STABLE
AS $$ SELECT count(*) FROM qualification.example_table $$;

CREATE PROCEDURE qualification.record_audit(p_action text)
LANGUAGE sql
AS $$ INSERT INTO qualification.audit_log(example_id, action)
      VALUES (NULL, p_action) $$;

INSERT INTO qualification.customer(external_id, display_name)
VALUES
    ('12345678-1234-5678-9abc-123456789001', 'George'),
    ('12345678-1234-5678-9abc-123456789002', 'Unicode — Καλημέρα');

INSERT INTO qualification.example_table(
    customer_id, code, payload, tags, mood, amount, occurred_at,
    local_stamp, notes, empty_text)
VALUES
    (1, 'alpha', '{"name":"first","active":true}', ARRAY['one','two'],
     'calm', 12.50, '2026-07-30T10:00:00+02',
     '2026-07-30 10:00:00', E'line one\nline two', ''),
    (2, 'βeta', '{"name":"second","active":false}', ARRAY['comma,value','quote"value'],
     'focused', 99.95, '2026-07-30T11:00:00+02',
     '2026-07-30 11:00:00', NULL, '');

CREATE VIEW qualification.example_view AS
SELECT id, code, mood, amount
FROM qualification.example_table;

CREATE MATERIALIZED VIEW qualification.example_summary AS
SELECT mood, count(*) AS row_count
FROM qualification.example_table
GROUP BY mood;

CREATE TABLE qualification.import_existing (
    id integer PRIMARY KEY,
    external_id uuid NOT NULL,
    payload jsonb NOT NULL,
    tags text[] NOT NULL,
    mood qualification.mood NOT NULL,
    amount qualification.positive_amount NOT NULL,
    occurred_at timestamp with time zone NOT NULL,
    local_stamp timestamp without time zone NOT NULL,
    notes text,
    empty_text text NOT NULL
);

CREATE TABLE qualification.large_transfer (
    id integer PRIMARY KEY,
    payload text NOT NULL,
    amount numeric(12,2) NOT NULL
);

INSERT INTO qualification.large_transfer(id, payload, amount)
SELECT value,
       repeat(md5(value::text), 4),
       (value % 10000)::numeric / 100
FROM generate_series(1, 100000) AS value;

DO $$
DECLARE
    value integer;
BEGIN
    FOR value IN 1..305 LOOP
        EXECUTE format(
            'CREATE TABLE qualification.large_object_%s '
            '(id integer PRIMARY KEY, value text)',
            lpad(value::text, 3, '0'));
    END LOOP;
END
$$;

CREATE TABLE qualification.disposable_delete_target (
    id integer PRIMARY KEY,
    value text
);

ANALYZE qualification.example_table;
ANALYZE qualification.large_transfer;

RESET search_path;
