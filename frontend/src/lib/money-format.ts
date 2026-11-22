/**
 * Exact ZAR formatting for amounts that travel as invariant two-decimal
 * strings. JavaScript Number is never used: money may exceed the safe integer
 * range, and binary floating point must not round a displayed total.
 */

const AMOUNT_PATTERN = /^(-?)(\d+)(?:\.(\d+))?$/;

type ParsedAmount = {
  negative: boolean;
  cents: bigint;
};

function parseAmount(value: string): ParsedAmount {
  const match = AMOUNT_PATTERN.exec(value.trim());
  if (!match) {
    throw new Error("invalid amount");
  }

  const [, sign, whole, fraction = ""] = match;
  const padded = (fraction + "00").slice(0, 2);
  let cents = BigInt(whole) * BigInt(100) + BigInt(padded);

  if (fraction.length > 2) {
    const next = Number(fraction[2]);
    if (next >= 5) {
      cents += BigInt(1);
    }
  }

  const negative = sign === "-" && cents !== BigInt(0);
  return { negative, cents };
}

function groupThousands(digits: string): string {
  let result = "";
  for (let index = 0; index < digits.length; index += 1) {
    if (index > 0 && (digits.length - index) % 3 === 0) {
      result += " ";
    }
    result += digits[index];
  }
  return result;
}

/** Formats an exact amount as `R 1 234.56`; a negative zero prints as zero. */
export function formatMoney(value: string): string {
  const { negative, cents } = parseAmount(value);
  const whole = cents / BigInt(100);
  const fraction = (cents % BigInt(100)).toString().padStart(2, "0");
  const body = `R ${groupThousands(whole.toString())}.${fraction}`;
  return negative ? `-${body}` : body;
}

/** Formats a signed delta with an explicit plus sign for positive values. */
export function formatSignedMoney(value: string): string {
  const { negative, cents } = parseAmount(value);
  if (cents === BigInt(0)) {
    return formatMoney("0.00");
  }
  const formatted = formatMoney(value);
  return negative ? formatted : `+${formatted}`;
}

/** Canonical two-decimal string without a currency prefix or grouping. */
export function normalizeAmount(value: string): string {
  const { negative, cents } = parseAmount(value);
  const whole = cents / BigInt(100);
  const fraction = (cents % BigInt(100)).toString().padStart(2, "0");
  return `${negative ? "-" : ""}${whole.toString()}.${fraction}`;
}

/** Compares exact signed amounts without converting money to JavaScript Number. */
export function compareMoneyDescending(left: string, right: string): number {
  const leftAmount = parseAmount(left);
  const rightAmount = parseAmount(right);
  const leftCents = leftAmount.negative ? -leftAmount.cents : leftAmount.cents;
  const rightCents = rightAmount.negative ? -rightAmount.cents : rightAmount.cents;

  if (leftCents === rightCents) {
    return 0;
  }
  return leftCents > rightCents ? -1 : 1;
}

export function isZeroAmount(value: string): boolean {
  return parseAmount(value).cents === BigInt(0);
}

/** Exact sum of invariant two-decimal amount strings. */
export function sumMoney(values: string[]): string {
  let cents = BigInt(0);
  for (const value of values) {
    const parsed = parseAmount(value);
    cents += parsed.negative ? -parsed.cents : parsed.cents;
  }

  const negative = cents < BigInt(0);
  const magnitude = negative ? -cents : cents;
  const whole = magnitude / BigInt(100);
  const fraction = (magnitude % BigInt(100)).toString().padStart(2, "0");
  return `${negative ? "-" : ""}${whole.toString()}.${fraction}`;
}

/** One-decimal percentage string from the backend, or an explicit absence. */
export function formatPercentage(value: string | null): string | null {
  if (value === null) {
    return null;
  }
  if (!/^-?\d+(\.\d+)?$/.test(value)) {
    return null;
  }
  return `${value}%`;
}
