const { chromium } = require('playwright');
const { loadTestCredentials } = require('./test-credentials');

(async () => {
  console.log('🚀 Starting MS Login Test...');
  
  const browser = await chromium.launch({ 
    headless: false,
    timeout: 30000
  });
  const context = await browser.newContext();
  const page = await context.newPage();
  
  let testResult = 'UNKNOWN';
  
  try {
    console.log('📱 Navigating to localhost:3000...');
    await page.goto('http://localhost:3000', { timeout: 15000 });
    await page.waitForLoadState('networkidle', { timeout: 10000 });
    
    // Take screenshot of initial state
    await page.screenshot({ path: '../../playwright-screenshots/initial_state.png' });
    
    // Check current authentication state
    const userInitialsVisible = await page.locator('span').filter({ hasText: /^OP$/ }).isVisible().catch(() => false);
    const signInButton = page.locator('button:has-text("Sign in")');
    const signInButtonCount = await signInButton.count();
    
    console.log(`🔍 User initials visible: ${userInitialsVisible}`);
    console.log(`🔍 Sign in buttons found: ${signInButtonCount}`);
    
    if (userInitialsVisible) {
      console.log('✅ USER ALREADY LOGGED IN');
      testResult = 'ALREADY_LOGGED_IN';
      
      // Test user profile functionality
      const success = await testUserProfile(page);
      testResult = success ? 'PROFILE_TEST_PASSED' : 'PROFILE_TEST_FAILED';
      
    } else if (signInButtonCount > 0) {
      console.log('🔐 TESTING LOGIN FLOW');
      testResult = 'LOGIN_FLOW_STARTED';
      
      console.log('👆 Clicking sign in button...');
      await signInButton.first().click();
      
      // Wait for redirect with shorter timeout to avoid hanging
      console.log('⏳ Waiting for MS login redirect...');
      try {
        await page.waitForURL(/login\.microsoftonline\.com/, { timeout: 8000 });
        console.log('✅ Successfully redirected to Microsoft login');
        testResult = 'MS_REDIRECT_SUCCESS';
        
        // Perform login
        const loginSuccess = await performMSLogin(page);
        if (loginSuccess) {
          testResult = 'LOGIN_SUCCESS';
        } else {
          testResult = 'LOGIN_FAILED';
        }
        
      } catch (redirectError) {
        console.log('❌ Failed to redirect to MS login');
        testResult = 'MS_REDIRECT_FAILED';
        await page.screenshot({ path: '../../playwright-screenshots/redirect_failed.png' });
      }
      
    } else {
      console.log('❌ NO AUTH ELEMENTS FOUND');
      testResult = 'NO_AUTH_ELEMENTS';
      await page.screenshot({ path: '../../playwright-screenshots/no_auth_elements.png' });
    }
    
  } catch (error) {
    console.error('❌ Test error:', error.message);
    testResult = 'TEST_ERROR';
    await page.screenshot({ path: '../../playwright-screenshots/test_error.png' });
  }
  
  console.log(`\n🏁 FINAL TEST RESULT: ${testResult}`);
  
  console.log('⏱️  Keeping browser open for 5 seconds...');
  await page.waitForTimeout(5000);
  
  await browser.close();
  console.log('✅ Test completed');
  
  // Exit with appropriate code
  process.exit(testResult.includes('SUCCESS') || testResult.includes('PASSED') || testResult === 'ALREADY_LOGGED_IN' ? 0 : 1);
})();

async function performMSLogin(page) {
  try {
    await page.waitForLoadState('networkidle', { timeout: 5000 });
    
    // Load credentials securely
    const credentials = loadTestCredentials();
    
    // Enter email
    const emailInput = page.locator('input[type="email"], input[name="loginfmt"]');
    if (await emailInput.count() === 0) {
      console.log('❌ Email input not found');
      return false;
    }
    
    await emailInput.fill(credentials.email);
    console.log('📝 Email entered');
    
    // Click Next
    const nextButton = page.locator('input[type="submit"], button:has-text("Next")');
    await nextButton.click();
    await page.waitForLoadState('networkidle', { timeout: 5000 });
    
    // Enter password
    const passwordInput = page.locator('input[type="password"], input[name="passwd"]');
    if (await passwordInput.count() === 0) {
      console.log('❌ Password input not found');
      return false;
    }
    
    await passwordInput.fill(credentials.password);
    console.log('🔑 Password entered');
    
    // Click Sign In
    const signInButton = page.locator('input[type="submit"], button:has-text("Sign in")');
    await signInButton.click();
    
    // Handle "Stay signed in?" prompt
    await page.waitForTimeout(3000);
    const staySignedInYes = page.locator('button:has-text("Yes"), input[value="Yes"]');
    if (await staySignedInYes.count() > 0) {
      console.log('💾 Clicking "Stay signed in" - Yes');
      await staySignedInYes.click();
    }
    
    // Wait for redirect back to app
    console.log('⏳ Waiting for redirect back to app...');
    await page.waitForURL(/localhost:3000/, { timeout: 10000 });
    console.log('✅ Back in application');
    
    // Wait for authentication to process
    await page.waitForTimeout(5000);
    
    return true;
    
  } catch (error) {
    console.log('❌ MS login failed:', error.message);
    return false;
  }
}

async function testUserProfile(page) {
  console.log('👤 Testing user profile...');
  
  try {
    // Look for user initials with multiple selectors
    const initialsSelectors = [
      'span:text("OP")',
      'span:has-text("OP")',
      '[class*="initial"]',
      'button span:text("OP")'
    ];
    
    let found = false;
    for (const selector of initialsSelectors) {
      try {
        await page.waitForSelector(selector, { timeout: 3000 });
        console.log(`✅ User initials found with selector: ${selector}`);
        found = true;
        break;
      } catch {
        // Try next selector
      }
    }
    
    if (!found) {
      console.log('❌ User initials not found with any selector');
      // Debug: show what's actually there
      const allSpans = await page.locator('span').allTextContents();
      console.log('🔍 All span texts:', allSpans.slice(0, 10));
      return false;
    }
    
    // Try to find and click user profile button
    const profileButtonSelectors = [
      'button:has(span:text("OP"))',
      'button:has-text("OP")',
      '[title*="@"]'
    ];
    
    for (const selector of profileButtonSelectors) {
      const button = page.locator(selector);
      if (await button.count() > 0) {
        console.log(`👆 Clicking user profile with selector: ${selector}`);
        await button.first().click();
        await page.waitForTimeout(1000);
        
        // Check for logout button
        const signOutButton = page.locator('button:has-text("Sign out")');
        if (await signOutButton.count() > 0) {
          console.log('✅ User menu opened successfully');
          console.log('✅ Sign out button found');
          return true;
        }
        break;
      }
    }
    
    console.log('⚠️  Could not open user menu');
    return false;
    
  } catch (error) {
    console.log('❌ User profile test failed:', error.message);
    return false;
  }
}