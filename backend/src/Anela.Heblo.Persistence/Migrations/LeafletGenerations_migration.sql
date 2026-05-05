-- Migration: Add LeafletGenerations table
-- Run manually against the dev database

CREATE TABLE IF NOT EXISTS public."LeafletGenerations" (
    "Id" uuid NOT NULL,
    "Topic" varchar(200) NOT NULL,
    "Audience" varchar(50) NOT NULL,
    "Length" varchar(50) NOT NULL,
    "FinalMarkdown" text NOT NULL,
    "KbSourceCount" integer NOT NULL,
    "LeafletSourceCount" integer NOT NULL,
    "DurationMs" bigint NOT NULL,
    "CreatedAt" timestamptz NOT NULL,
    "UserId" varchar(200) NULL,
    "PrecisionScore" integer NULL,
    "StyleScore" integer NULL,
    "FeedbackComment" text NULL,
    CONSTRAINT "PK_LeafletGenerations" PRIMARY KEY ("Id")
);

CREATE INDEX IF NOT EXISTS "IX_LeafletGenerations_CreatedAt" ON public."LeafletGenerations" ("CreatedAt");
CREATE INDEX IF NOT EXISTS "IX_LeafletGenerations_UserId" ON public."LeafletGenerations" ("UserId");
CREATE INDEX IF NOT EXISTS "IX_LeafletGenerations_PrecisionScore" ON public."LeafletGenerations" ("PrecisionScore") WHERE "PrecisionScore" IS NOT NULL;
