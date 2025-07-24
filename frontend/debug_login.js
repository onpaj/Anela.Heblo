const { chromium } = require('playwright');

(async () => {
  console.log('Starting browser...');
  const browser = await chromium.launch({ 
    headless: false,
    args: ['--disable-web-security', '--disable-features=VizDisplayCompositor']
  });
  
  const page = await browser.newPage();
  
  console.log('Navigating to localhost:3000...');
  await page.goto('http://localhost:3000');
  
  // Wait for page to load
  await page.waitForLoadState('networkidle');
  
  console.log('Taking screenshot of initial state...');
  await page.screenshot({ path: 'initial_state.png' });
  
  // Check console errors and logs
  page.on('console', msg => {
    console.log(`Console ${msg.type()}: ${msg.text()}`);
  });
  
  // Check for network requests
  page.on('request', request => {
    if (request.url().includes('login.microsoftonline.com')) {
      console.log('Auth request:', request.url());
    }
  });
  
  page.on('response', response => {
    if (response.url().includes('login.microsoftonline.com')) {
      console.log('Auth response:', response.status(), response.url());
    }
  });
  
  // Look for sign in button
  console.log('Looking for sign in button...');
  
  try {
    // Try different selectors for the sign in button
    const signInButton = await page.locator('button:has-text("Sign in")').first();
    
    if (await signInButton.count() > 0) {
      console.log('Found sign in button, clicking...');
      
      // Listen for popup
      const popupPromise = page.waitForEvent('popup');
      await signInButton.click();
      
      console.log('Waiting for popup...');
      const popup = await popupPromise;
      
      console.log('Popup opened!');
      console.log('Popup URL:', await popup.url());
      
      // Wait for popup to load
      await popup.waitForLoadState('networkidle');
      
      console.log('Taking popup screenshot...');
      await popup.screenshot({ path: 'popup_state.png' });
      
      console.log('Popup title:', await popup.title());
      
      // Keep browser open for a few seconds to observe
      await page.waitForTimeout(5000);
      
    } else {
      console.log('No sign in button found');
      
      // Let's see what's actually on the page
      const pageContent = await page.content();
      console.log('Page HTML length:', pageContent.length);
      
      // Look for any buttons
      const buttons = await page.locator('button').all();
      console.log('Found buttons:', buttons.length);
      
      for (let i = 0; i < buttons.length; i++) {
        const text = await buttons[i].textContent();
        console.log(`Button ${i}: "${text}"`);
      }
    }
    
  } catch (error) {
    console.error('Error during test:', error.message);
    await page.screenshot({ path: 'error_state.png' });
  }
  
  await browser.close();
  console.log('Test completed');
})();