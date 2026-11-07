import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/auth", () => ({
  auth: vi.fn(),
}));

vi.mock("next/cache", () => ({
  revalidatePath: vi.fn(),
}));

vi.mock("@/lib/server/backend-client", () => ({
  abandonTransactionOperation: vi.fn(),
  applyTransactionOperation: vi.fn(),
  getTransaction: vi.fn(),
  getTransactionOperation: vi.fn(),
  patchTransaction: vi.fn(),
  prepareTransactionOperation: vi.fn(),
  queryTransactions: vi.fn(),
}));

import { revalidatePath } from "next/cache";

import nextConfig from "../../../next.config";
import { auth } from "@/auth";
import {
  abandonOperationAction,
  applyOperationAction,
  loadOperationAction,
  loadTransactionAction,
  prepareTransactionsAction,
  queryTransactionsAction,
  saveTransactionAction,
} from "@/lib/server/transaction-actions";
import {
  abandonTransactionOperation,
  applyTransactionOperation,
  getTransaction,
  getTransactionOperation,
  patchTransaction,
  prepareTransactionOperation,
  queryTransactions,
} from "@/lib/server/backend-client";

function signedIn(token = "backend-token") {
  vi.mocked(auth).mockResolvedValue({
    expires: "2030-01-01T00:00:00.000Z",
    authenticated: true,
    user: { backendToken: token },
  } as never);
}

const page = {
  transactions: [],
  inventory_count: 0,
  matching_visible_count: 0,
  matching_hidden_count: 0,
  matching_total: 0,
  filtered_total: 0,
  page: 1,
  page_size: 25,
  sort_by: "date",
  sort_direction: "desc",
};

const operationId = "11111111-1111-1111-1111-111111111111";
const transactionId = "22222222-2222-2222-2222-222222222222";

describe("transaction server actions", () => {
  it("keeps framework Server Function argument logging disabled", () => {
    const logging = nextConfig.logging;
    expect(logging && typeof logging === "object" && logging.serverFunctions).toBe(false);
  });

  beforeEach(() => {
    vi.mocked(auth).mockReset();
    vi.mocked(revalidatePath).mockReset();
    vi.mocked(abandonTransactionOperation).mockReset();
    vi.mocked(applyTransactionOperation).mockReset();
    vi.mocked(getTransaction).mockReset();
    vi.mocked(getTransactionOperation).mockReset();
    vi.mocked(patchTransaction).mockReset();
    vi.mocked(prepareTransactionOperation).mockReset();
    vi.mocked(queryTransactions).mockReset();
  });

  it("requires a session instead of calling the backend", async () => {
    vi.mocked(auth).mockResolvedValue(null as never);

    await expect(queryTransactionsAction({})).resolves.toMatchObject({
      ok: false,
      kind: "unauthorized",
    });
    expect(queryTransactions).not.toHaveBeenCalled();
  });

  it("rejects malformed queries and passes validated filters", async () => {
    signedIn();
    vi.mocked(queryTransactions).mockResolvedValue({ ok: true, value: page });

    await expect(
      queryTransactionsAction({
        filters: { search: "x".repeat(201) },
      }),
    ).resolves.toMatchObject({ ok: false, kind: "invalid_request" });
    expect(queryTransactions).not.toHaveBeenCalled();

    await expect(
      queryTransactionsAction({
        filters: { merchant_tag: "untagged", visibility: "hidden" },
        page: 2,
        page_size: 50,
        sort_by: "amount",
        sort_direction: "asc",
      }),
    ).resolves.toEqual({ ok: true, value: page });
    expect(queryTransactions).toHaveBeenCalledWith("backend-token", {
      filters: { merchant_tag: "untagged", visibility: "hidden" },
      page: 2,
      page_size: 50,
      sort_by: "amount",
      sort_direction: "asc",
    });
  });

  it("requires confirmation for hide/restore and revalidates after a save", async () => {
    signedIn();
    vi.mocked(patchTransaction).mockResolvedValue({
      ok: true,
      value: {
        transaction: { id: transactionId },
        selected_count: 1,
        changed_count: 1,
        unchanged_count: 0,
      } as never,
    });

    await expect(
      saveTransactionAction({
        id: transactionId,
        expected_version: 2,
        changes: { is_hidden_by_user: true },
      }),
    ).resolves.toMatchObject({ ok: false, kind: "invalid_request" });
    expect(patchTransaction).not.toHaveBeenCalled();

    const result = await saveTransactionAction({
      id: transactionId,
      expected_version: 2,
      changes: { is_hidden_by_user: true },
      confirm: true,
    });
    expect(result.ok).toBe(true);
    expect(patchTransaction).toHaveBeenCalledWith("backend-token", transactionId, {
      expected_version: 2,
      changes: { is_hidden_by_user: true },
      confirm: true,
    });
    expect(revalidatePath).toHaveBeenCalledWith("/transactions");
    expect(revalidatePath).toHaveBeenCalledWith("/imports");
    expect(revalidatePath).toHaveBeenCalledWith("/settings");
    expect(revalidatePath).toHaveBeenCalledWith("/home");
  });

  it("validates bulk selection before preparing", async () => {
    signedIn();
    vi.mocked(prepareTransactionOperation).mockResolvedValue({
      ok: true,
      value: { operation: { operation_id: operationId }, created: true } as never,
    });

    await expect(
      prepareTransactionsAction({
        operation_id: operationId,
        selection: {
          mode: "explicit",
          transactions: [
            { id: transactionId, expected_version: 1 },
            { id: transactionId, expected_version: 1 },
          ],
        },
        changes: { is_hidden_by_user: true },
      }),
    ).resolves.toMatchObject({ ok: false, kind: "invalid_request" });
    expect(prepareTransactionOperation).not.toHaveBeenCalled();

    await expect(
      prepareTransactionsAction({
        operation_id: operationId,
        selection: {
          mode: "explicit",
          transactions: [{ id: transactionId, expected_version: 1 }],
        },
        changes: { is_hidden_by_user: true },
      }),
    ).resolves.toMatchObject({ ok: true });
    expect(prepareTransactionOperation).toHaveBeenCalledWith("backend-token", {
      operation_id: operationId,
      selection: {
        mode: "explicit",
        transactions: [{ id: transactionId, expected_version: 1 }],
      },
      changes: { is_hidden_by_user: true },
    });
  });

  it("preserves failure details and never leaks backend bodies", async () => {
    signedIn();
    vi.mocked(applyTransactionOperation).mockResolvedValue({
      ok: false,
      kind: "parent_hidden",
      details: { statements: [{ id: "stmt-1", statement_label: "Statement" }] },
    });

    const result = await applyOperationAction(operationId, "hash", 1);
    expect(result).toMatchObject({
      ok: false,
      kind: "parent_hidden",
      details: { statements: [{ id: "stmt-1", statement_label: "Statement" }] },
    });
    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.message).toMatch(/parent statement is hidden/i);
    }

    signedIn();
    vi.mocked(getTransaction).mockResolvedValue({ ok: false, kind: "not_found" });
    await expect(loadTransactionAction(transactionId)).resolves.toMatchObject({
      ok: false,
      kind: "not_found",
    });

    signedIn();
    vi.mocked(getTransactionOperation).mockResolvedValue({
      ok: false,
      kind: "operation_expired",
    });
    await expect(loadOperationAction(operationId)).resolves.toMatchObject({
      ok: false,
      kind: "operation_expired",
    });

    signedIn();
    vi.mocked(abandonTransactionOperation).mockRejectedValue(new Error("secret detail"));
    await expect(abandonOperationAction(operationId)).resolves.toMatchObject({
      ok: false,
      kind: "unavailable",
    });
  });
});
