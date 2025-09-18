import { test, expect } from '@playwright/test';

test('test', async ({ page }) => {
  await page.goto('https://login.microsoftonline.com/31fd4df1-b9c0-4abd-a4b0-0e1aceaabe9a/oauth2/v2.0/authorize?client_id=87193df0-3128-44d2-8673-580e97631a07&scope=User.Read%20openid%20profile%20offline_access&redirect_uri=https%3A%2F%2Fheblo.stg.anela.cz&client-request-id=01995dfe-e203-7d57-9f1f-1a761869f4b8&response_mode=fragment&response_type=code&x-client-SKU=msal.js.browser&x-client-VER=3.28.1&client_info=1&code_challenge=33QKdM7EiIPngwX7r13Dh2V2TcRh5yXR80thfFROHLw&code_challenge_method=S256&prompt=select_account&nonce=01995dfe-e204-7604-b89e-4f68da352e7e&state=eyJpZCI6IjAxOTk1ZGZlLWUyMDMtNzExZC04OGU0LTkyYWJlNWY0N2QyOSIsIm1ldGEiOnsiaW50ZXJhY3Rpb25UeXBlIjoicmVkaXJlY3QifX0%3D&claims=%7B%22access_token%22%3A%7B%22xms_cc%22%3A%7B%22values%22%3A%5B%22CP1%22%5D%7D%7D%7D&sso_reload=true');
  await page.getByRole('textbox', { name: 'Enter your email, phone, or' }).click();
  await page.getByRole('textbox', { name: 'Enter your email, phone, or' }).fill('ondra@anela.cz');
  await page.getByRole('textbox', { name: 'Enter your email, phone, or' }).press('Enter');
  await page.getByRole('button', { name: 'Next' }).click();
  await page.getByRole('textbox', { name: 'Enter the password for ondra@' }).fill('2hVv6I17jlYddjS5V');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.getByRole('button', { name: 'Yes' }).click();
  await page.getByRole('button', { name: 'Výroba' }).click();
  await page.getByRole('link', { name: 'Kalkulačka dávek' }).click();
  await page.locator('.css-18w4uv4').click();
  await page.locator('#react-select-2-input').fill('DEO001001M');
  await page.locator('#react-select-2-input').press('Enter');
  await page.getByPlaceholder('0.00').click();
  await page.getByPlaceholder('0.00').fill('10000');
  await page.getByPlaceholder('0.00').press('Enter');
  await page.getByRole('button', { name: 'Přejít na plánování výroby' }).click();
  page.once('dialog', dialog => {
    console.log(`Dialog message: ${dialog.message()}`);
    dialog.dismiss().catch(() => {});
  });
  await page.getByRole('button', { name: 'Vytvořit zakázku' }).click();
});