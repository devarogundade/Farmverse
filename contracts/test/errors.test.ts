import { describe, expect, it } from 'vitest';
import { CLIENT_ONLY_ERRORS, ERROR_CATALOGUE, ErrorBody, ErrorCode } from '../src';

describe('error codes', () => {
  it('are SCREAMING_SNAKE_CASE with unique camelCase parameters', () => {
    for (const [code, params] of Object.entries(ERROR_CATALOGUE)) {
      expect(code).toMatch(/^[A-Z][A-Z0-9_]*$/);
      expect(new Set(params).size).toBe(params.length);
      for (const param of params) expect(param).toMatch(/^[a-z][A-Za-z0-9]*$/);
    }
  });

  it('includes every client-only code in the enum', () => {
    for (const code of CLIENT_ONLY_ERRORS) expect(ErrorCode.options).toContain(code);
  });

  it('only accepts string parameters', () => {
    expect(ErrorBody.safeParse({ code: 'CLIENT_OUTDATED', params: { minVersion: '2.0.0' } }).success).toBe(true);
    expect(ErrorBody.safeParse({ code: 'CLIENT_OUTDATED', params: { minVersion: 2 } }).success).toBe(false);
    expect(ErrorBody.safeParse({ code: 'NOT_A_CODE', params: {} }).success).toBe(false);
  });
});
