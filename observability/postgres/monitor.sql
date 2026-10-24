CREATE EXTENSION IF NOT EXISTS pg_stat_statements;

SELECT 'CREATE ROLE ratio_monitor'
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'ratio_monitor')
\gexec

ALTER ROLE ratio_monitor WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE CONNECTION LIMIT 5 PASSWORD :'password';
GRANT pg_monitor TO ratio_monitor;
GRANT CONNECT ON DATABASE :"database" TO ratio_monitor;
