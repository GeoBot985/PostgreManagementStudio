\set ON_ERROR_STOP on

SET ROLE :"app_role";

DO $performance_seed$
BEGIN
    FOR schema_number IN 1..5 LOOP
        EXECUTE format('CREATE SCHEMA pms_perf_%s', schema_number);
    END LOOP;

    FOR object_number IN 1..1000 LOOP
        EXECUTE format(
            'CREATE TABLE pms_perf_%s.table_%s (id bigint PRIMARY KEY, payload text, created_at timestamptz)',
            ((object_number - 1) % 5) + 1,
            lpad(object_number::text, 4, '0'));
    END LOOP;

    FOR object_number IN 1..250 LOOP
        EXECUTE format(
            'CREATE VIEW pms_perf_%s.view_%s AS SELECT id, payload FROM pms_perf_%s.table_%s',
            ((object_number - 1) % 5) + 1,
            lpad(object_number::text, 4, '0'),
            ((object_number - 1) % 5) + 1,
            lpad(object_number::text, 4, '0'));
        EXECUTE format(
            'CREATE FUNCTION pms_perf_%s.function_%s(integer) RETURNS integer LANGUAGE sql IMMUTABLE AS %L',
            ((object_number - 1) % 5) + 1,
            lpad(object_number::text, 4, '0'),
            format('SELECT $1 + %s', object_number));
    END LOOP;

    FOR object_number IN 1..20 LOOP
        EXECUTE format(
            'CREATE INDEX table_%s_payload_idx ON pms_perf_1.table_%s (payload)',
            lpad(object_number::text, 4, '0'),
            lpad((((object_number - 1) * 5) + 1)::text, 4, '0'));
    END LOOP;
END
$performance_seed$;

INSERT INTO pms_perf_1.table_0001 (id, payload, created_at)
SELECT value, repeat(md5(value::text), 2), clock_timestamp()
FROM generate_series(1, 100000) AS value;

CREATE TABLE pms_perf_1.partitioned_events
(
    event_id bigint NOT NULL,
    occurred_on date NOT NULL,
    payload jsonb
) PARTITION BY HASH (event_id);

DO $partitions$
BEGIN
    FOR partition_number IN 0..15 LOOP
        EXECUTE format(
            'CREATE TABLE pms_perf_1.partitioned_events_%s PARTITION OF pms_perf_1.partitioned_events FOR VALUES WITH (MODULUS 16, REMAINDER %s)',
            partition_number,
            partition_number);
    END LOOP;
END
$partitions$;

CREATE TABLE pms_perf_1.large_values
(
    id integer PRIMARY KEY,
    large_text text NOT NULL,
    large_json jsonb NOT NULL,
    large_binary bytea NOT NULL,
    large_array integer[] NOT NULL
);

INSERT INTO pms_perf_1.large_values
VALUES
(
    1,
    repeat('large-value-', 350000),
    jsonb_build_object('payload', repeat('json-value-', 300000)),
    decode(repeat('ab', 2000000), 'hex'),
    ARRAY(SELECT value FROM generate_series(1, 100000) AS value)
);

CREATE TABLE pms_perf_1.million_row_source(seed integer PRIMARY KEY);
INSERT INTO pms_perf_1.million_row_source
SELECT value FROM generate_series(1, 1000) AS value;

CREATE TABLE pms_perf_1."Object requiring quoting with a deliberately long name 0123456789"
(
    "Column requiring quoting" text
);

ANALYZE pms_perf_1.million_row_source;
ANALYZE pms_perf_1.large_values;
ANALYZE pms_perf_1.table_0001;

RESET ROLE;
