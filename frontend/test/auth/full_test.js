const { chromium } = require('playwright');
const { loadTestCredentials } = require('./test-credentials');

(async () => {
  console.log('🚀 Starting Full Authentication Test...');
  
  const browser = await chromium.launch({ headless: false });
  const context = await browser.newContext();
  const page = await context.newPage();
  
  let testResult = 'UNKNOWN';
  
  try {
    console.log('📱 Navigating to localhost:3000...');
    await page.goto('http://localhost:3000', { timeout: 15000 });
    await page.waitForLoadState('networkidle', { timeout: 10000 });
    
    // Step 1: Test login
    console.log('\n=== STEP 1: TESTING LOGIN ===');
    const loginSuccess = await performLogin(page);
    
    if (loginSuccess) {
      console.log('✅ Login successful');
      
      // Step 2: Test profile functionality
      console.log('\n=== STEP 2: TESTING PROFILE ===');
      const profileSuccess = await testUserProfile(page);
      
      if (profileSuccess) {
        testResult = 'FULL_TEST_PASSED';
      } else {
        testResult = 'PROFILE_TEST_FAILED';
      }
      
    } else {
      testResult = 'LOGIN_FAILED';
    }
    
  } catch (error) {
    console.error('❌ Test error:', error.message);
    testResult = 'TEST_ERROR';
    await page.screenshot({ path: '../../playwright-screenshots/full_test_error.png' });
  }
  
  console.log(`\n🏁 FINAL TEST RESULT: ${testResult}`);
  
  console.log('⏱️  Keeping browser open for 8 seconds...');
  await page.waitForTimeout(8000);
  
  await browser.close();
  console.log('✅ Test completed');
  
  // Exit with appropriate code
  process.exit(testResult === 'FULL_TEST_PASSED' ? 0 : 1);
})();

async function performLogin(page) {
  try {
    // Check if already logged in
    const userInitialsVisible = await page.locator('span').filter({ hasText: /^OP$/ }).isVisible().catch(() => false);
    
    if (userInitialsVisible) {
      console.log('✅ User already logged in');
      return true;
    }
    
    // Find and click sign in button
    const signInButton = page.locator('button:has-text("Sign in")');
    const signInButtonCount = await signInButton.count();
    
    if (signInButtonCount === 0) {
      console.log('❌ Sign in button not found');
      return false;
    }
    
    console.log('👆 Clicking sign in button...');
    await signInButton.first().click();
    
    // Wait for redirect to MS login
    console.log('⏳ Waiting for MS login redirect...');
    await page.waitForURL(/login\.microsoftonline\.com/, { timeout: 8000 });
    console.log('✅ Redirected to Microsoft login');
    
    // Perform MS login
    return await performMSLogin(page);
    
  } catch (error) {
    console.log('❌ Login failed:', error.message);
    return false;
  }
}

async function performMSLogin(page) {
  try {
    await page.waitForLoadState('networkidle', { timeout: 5000 });
    
    // Load credentials securely
    const credentials = loadTestCredentials();
    
    // Enter email
    const emailInput = page.locator('input[type="email"], input[name="loginfmt"]');
    await emailInput.fill(credentials.email);
    console.log('📝 Email entered');
    
    // Click Next
    const nextButton = page.locator('input[type="submit"], button:has-text("Next")');
    await nextButton.click();
    await page.waitForLoadState('networkidle', { timeout: 5000 });
    
    // Enter password
    const passwordInput = page.locator('input[type="password"], input[name="passwd"]');
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
    console.log('⏳ Waiting for authentication to process...');
    await page.waitForTimeout(8000);
    
    return true;
    
  } catch (error) {
    console.log('❌ MS login failed:', error.message);
    return false;
  }
}

async function testUserProfile(page) {
  console.log('👤 Testing user profile functionality...');
  
  try {
    // Wait a bit more for UI to update
    await page.waitForTimeout(2000);
    
    // Take screenshot to see current state
    await page.screenshot({ path: '../../playwright-screenshots/before_profile_test.png' });
    
    // Check if user initials are visible
    const userInitials = page.locator('span').filter({ hasText: /^OP$/ });
    const isVisible = await userInitials.isVisible().catch(() => false);
    
    console.log(`🔍 User initials visible: ${isVisible}`);
    
    if (!isVisible) {
      // Debug: check what's in the page
      const pageText = await page.textContent('body');
      console.log('🔍 Page contains "OP":', pageText.includes('OP'));
      console.log('🔍 Page contains "Sign in":', pageText.includes('Sign in'));
      
      // Look for any spans containing OP
      const allOPSpans = await page.locator('span:has-text("OP")').count();
      console.log(`🔍 Spans containing "OP": ${allOPSpans}`);
      
      return false;
    }
    
    console.log('✅ User initials found - user is logged in');
    
    // Try to click on user profile
    const profileButton = page.locator('button:has(span:text("OP"))');
    const profileCount = await profileButton.count();
    
    console.log(`🔍 Profile buttons found: ${profileCount}`);
    
    if (profileCount > 0) {
      console.log('👆 Clicking user profile...');
      await profileButton.first().click();
      await page.waitForTimeout(2000);
      
      // Take screenshot after click
      await page.screenshot({ path: '../../playwright-screenshots/after_profile_click.png' });
      
      // Check for logout button
      const logoutButton = page.locator('button:has-text("Sign out")');
      const logoutCount = await logoutButton.count();
      
      console.log(`🔍 Logout buttons found: ${logoutCount}`);
      
      if (logoutCount > 0) {
        console.log('✅ User menu opened successfully');
        console.log('✅ Sign out button found');
        return true;
      } else {
        console.log('❌ No logout button found after clicking profile');
        return false;
      }
    } else {
      console.log('❌ No clickable profile button found');
      return false;
    }
    
  } catch (error) {
    console.log('❌ User profile test failed:', error.message);
    return false;
  }
}