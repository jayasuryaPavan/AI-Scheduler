CREATE TABLE IF NOT EXISTS "ChatConversations" (
    "Id"          SERIAL PRIMARY KEY,
    "SessionId"   VARCHAR(100) NOT NULL,
    "Role"        VARCHAR(20)  NOT NULL CHECK ("Role" IN ('user', 'assistant')),
    "Content"     TEXT         NOT NULL,
    "ToolCalls"   JSONB        NOT NULL DEFAULT '[]',
    "Embedding"   vector(768),
    "CreatedAt"   TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_chat_session ON "ChatConversations" ("SessionId", "CreatedAt");
CREATE INDEX IF NOT EXISTS idx_chat_embedding ON "ChatConversations" USING hnsw ("Embedding" vector_cosine_ops);
