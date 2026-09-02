# v6.8.1 validation record

This note records the functional Windows validation used to consolidate the v6.8.1 network-discovery flow.

## Observed sequence

- Application started as `Garlic SaveMgr v6.8` during the validation build prior to the version bump.
- Ping batches were executed using native Windows `ping.exe`.
- The second batch produced 13 ICMP-positive hosts.
- Garlic was located at `192.168.1.211:8082`.
- The payload `garlic-savemgr v1.13` was sent to `192.168.1.211:9021` (`elfldr`).
- Garlic became available on `8082` after the payload load.
- The subsequent PS5 scan reported 41 titles.

The source log used for this record is the Windows execution log supplied during development on 2026-09-02.
