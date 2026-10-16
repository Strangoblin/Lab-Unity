-- Hermes-agent style FTS5 session search
-- For cross-session recall of past work

CREATE VIRTUAL TABLE IF NOT EXISTS session_fts USING fts5(
  session_id,
  timestamp,
  summary,
  content,
  tokenize='unicode61'
);

-- Index for date-based queries
CREATE INDEX IF NOT EXISTS idx_session_timestamp
  ON session_fts(timestamp);
