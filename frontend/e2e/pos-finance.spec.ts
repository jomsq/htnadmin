/**
 * Critical flows from CLAUDE.md that belong to later phases: merch POS sale and board finance posting.
 * The /store and /finance screens are "Coming soon" placeholders today, so these are listed as fixme and are not run.
 * Replace each fixme with a real test in the slice that builds the feature (see .claude/agents/qa-tester.md).
 */
import { test } from '@playwright/test'

test.describe('POS and finance (later phases)', () => {
  test.fixme('POS sale with a discount posts revenue to the ledger', async () => {})
  test.fixme('finance posting: a month cannot close while unbalanced', async () => {})
})
