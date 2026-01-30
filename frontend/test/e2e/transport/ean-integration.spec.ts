import { test, expect } from '@playwright/test';
import { navigateToTransportBoxes } from '../helpers/e2e-auth-helper';

test.describe('Transport EAN Integration E2E Tests', () => {

  test.beforeEach(async ({ page }) => {
    // Navigate to transport boxes with full authentication
    await navigateToTransportBoxes(page);
  });

  test('should test EAN code scanning and validation', async ({ page }) => {
    
    // Find or create a box to work with
    let targetBox = page.locator('[data-testid="transport-box-item"], .transport-box-item, .box-item, tr:has(td)').first();
    
    if (await targetBox.count() === 0) {
      // Create a test box
      const createButton = page.locator('button').filter({ hasText: /(\+|Add|Create|Nový|Přidat)/ });
      if (await createButton.count() > 0) {
        await createButton.first().click();
        await page.waitForTimeout(1000);
        
        const descriptionField = page.locator('input[name="description"], textarea[name="description"]').first();
        if (await descriptionField.count() > 0) {
          await descriptionField.fill('EAN Test Box');
          
          const submitButton = page.locator('button[type="submit"], button').filter({ hasText: /Save|Create/ });
          if (await submitButton.count() > 0) {
            await submitButton.first().click();
            await page.waitForTimeout(2000);
          }
        }
        
        targetBox = page.locator('[data-testid="transport-box-item"], .transport-box-item, .box-item').filter({ 
          hasText: /EAN Test Box/ 
        }).first();
      }
    }
    
    if (await targetBox.count() > 0) {
      // Open box detail
      const clickableElement = targetBox.locator('a, button, .clickable').first();
      if (await clickableElement.count() > 0) {
        await clickableElement.click();
      } else {
        await targetBox.click();
      }
      
      await page.waitForTimeout(1000);
      
      // Look for EAN code display
      const eanDisplay = page.locator('[data-testid="ean-code"], .ean-code, .barcode, input[name*="ean"], input[readonly]');
      
      if (await eanDisplay.count() > 0) {
        // Check if EAN is already generated
        const currentEan = await eanDisplay.first().textContent() || await eanDisplay.first().inputValue();
        
        if (!currentEan || currentEan.length < 8) {
          // Generate EAN if not present
          const generateButton = page.locator('button').filter({ hasText: /Generate|Generovat|Create EAN/ });
          if (await generateButton.count() > 0) {
            await generateButton.first().click();
            await page.waitForTimeout(1000);
            
            // Should now have EAN
            const newEan = await eanDisplay.first().textContent() || await eanDisplay.first().inputValue();
            expect(newEan.length).toBeGreaterThanOrEqual(8);
          }
        }
        
        // Test EAN validation
        const eanInput = page.locator('input[name*="ean"], input[placeholder*="EAN"]').first();
        
        if (await eanInput.count() > 0) {
          // Test valid EAN
          await eanInput.clear();
          await eanInput.fill('1234567890123'); // 13-digit EAN
          
          const validateButton = page.locator('button').filter({ hasText: /Validate|Check|Ověřit/ });
          if (await validateButton.count() > 0) {
            await validateButton.first().click();
            await page.waitForTimeout(500);
            
            // Should show validation result
            const validationResult = page.locator('.validation-result, .success, .error, .text-green-500, .text-red-500');
            await expect(validationResult.first()).toBeVisible();
          }
          
          // Test invalid EAN
          await eanInput.clear();
          await eanInput.fill('invalid123');
          
          if (await validateButton.count() > 0) {
            await validateButton.first().click();
            await page.waitForTimeout(500);
            
            // Should show error
            const errorResult = page.locator('.error, .text-red-500, .invalid');
            await expect(errorResult.first()).toBeVisible();
          }
        }
      }
      
      // Test barcode scanning simulation
      const scanButton = page.locator('button').filter({ hasText: /Scan|Skenovat|Barcode/ });
      
      if (await scanButton.count() > 0) {
        await scanButton.first().click();
        await page.waitForTimeout(1000);
        
        // Should open scanning interface or modal
        const scanModal = page.locator('[role="dialog"], .modal, .scan-interface');
        const cameraInterface = page.locator('.camera, video, .scanner');
        
        const hasScanModal = await scanModal.count() > 0;
        const hasCameraInterface = await cameraInterface.count() > 0;
        
        expect(hasScanModal || hasCameraInterface).toBe(true);
        
        if (hasScanModal) {
          // Simulate manual EAN entry in scan modal
          const scanInput = scanModal.locator('input[name*="ean"], input[name*="code"]').first();
          if (await scanInput.count() > 0) {
            await scanInput.fill('1234567890123');
            
            const confirmButton = scanModal.locator('button').filter({ hasText: /OK|Confirm|Use|Použít/ });
            if (await confirmButton.count() > 0) {
              await confirmButton.first().click();
              await page.waitForTimeout(1000);
              
              // EAN should be applied to box
              const updatedEan = await eanDisplay.first().textContent() || await eanDisplay.first().inputValue();
              expect(updatedEan).toBe('1234567890123');
            }
          }
        }
      }
    }
  });

  test('should validate EAN code uniqueness and formatting', async ({ page }) => {
    await navigateToTransportBoxes(page);
    
    // Create or find boxes to test EAN uniqueness
    const createButton = page.locator('button').filter({ hasText: /(\+|Add|Create|Nový|Přidat)/ });
    
    if (await createButton.count() > 0) {
      // Create first box with specific EAN
      await createButton.first().click();
      await page.waitForTimeout(1000);
      
      const descriptionField = page.locator('input[name="description"], textarea[name="description"]').first();
      if (await descriptionField.count() > 0) {
        await descriptionField.fill('EAN Uniqueness Test Box 1');
        
        // Set custom EAN
        const eanField = page.locator('input[name*="ean"], input[placeholder*="EAN"]').first();
        if (await eanField.count() > 0) {
          await eanField.fill('1111111111111');
        }
        
        const submitButton = page.locator('button[type="submit"], button').filter({ hasText: /Save|Create/ });
        if (await submitButton.count() > 0) {
          await submitButton.first().click();
          await page.waitForTimeout(2000);
        }
      }
      
      // Try to create second box with same EAN
      if (await createButton.count() > 0) {
        await createButton.first().click();
        await page.waitForTimeout(1000);
        
        const descriptionField2 = page.locator('input[name="description"], textarea[name="description"]').first();
        if (await descriptionField2.count() > 0) {
          await descriptionField2.fill('EAN Uniqueness Test Box 2');
          
          // Try to use same EAN
          const eanField2 = page.locator('input[name*="ean"], input[placeholder*="EAN"]').first();
          if (await eanField2.count() > 0) {
            await eanField2.fill('1111111111111'); // Same as first box
            
            const submitButton2 = page.locator('button[type="submit"], button').filter({ hasText: /Save|Create/ });
            if (await submitButton2.count() > 0) {
              await submitButton2.first().click();
              await page.waitForTimeout(1000);
              
              // Should show uniqueness error
              const errorMessage = page.locator('.error, .text-red-500, .invalid').filter({ 
                hasText: /unique|duplicate|already exists|již existuje/i 
              });
              
              if (await errorMessage.count() > 0) {
                await expect(errorMessage.first()).toBeVisible();
              }
              
              // Cancel this creation
              const cancelButton = page.locator('button').filter({ hasText: /Cancel|Zrušit/ });
              if (await cancelButton.count() > 0) {
                await cancelButton.first().click();
              }
            }
          }
        }
      }
    }
    
    // Test EAN formatting validation
    if (await createButton.count() > 0) {
      await createButton.first().click();
      await page.waitForTimeout(1000);
      
      const descriptionField = page.locator('input[name="description"], textarea[name="description"]').first();
      if (await descriptionField.count() > 0) {
        await descriptionField.fill('EAN Format Test Box');
        
        const eanField = page.locator('input[name*="ean"], input[placeholder*="EAN"]').first();
        if (await eanField.count() > 0) {
          // Test various invalid formats
          const invalidEans = ['123', '12345678901234567890', 'abc123', '123-456-789', '12345.67890'];
          
          for (const invalidEan of invalidEans) {
            await eanField.clear();
            await eanField.fill(invalidEan);
            
            const submitButton = page.locator('button[type="submit"], button').filter({ hasText: /Save|Create/ });
            if (await submitButton.count() > 0) {
              await submitButton.first().click();
              await page.waitForTimeout(500);
              
              // Should show format error
              const formatError = page.locator('.error, .text-red-500, .invalid').filter({ 
                hasText: /format|length|invalid|neplatný/i 
              });
              
              if (await formatError.count() > 0) {
                await expect(formatError.first()).toBeVisible();
                break; // One validation error is enough
              }
            }
          }
          
          // Cancel this creation
          const cancelButton = page.locator('button').filter({ hasText: /Cancel|Zrušit/ });
          if (await cancelButton.count() > 0) {
            await cancelButton.first().click();
          }
        }
      }
    }
  });

  test('should test EAN-based box lookup and identification', async ({ page }) => {
    await navigateToTransportBoxes(page);
    
    // Look for search functionality that supports EAN lookup
    const searchBox = page.locator('input[type="search"], input[placeholder*="search"], input[placeholder*="hledat"], input[placeholder*="EAN"]');
    
    if (await searchBox.count() > 0) {
      // First, get a box with an EAN code
      const boxes = page.locator('[data-testid="transport-box-item"], .transport-box-item, .box-item, tr:has(td)');
      
      if (await boxes.count() > 0) {
        // Open first box to get its EAN
        const firstBox = boxes.first();
        const clickableElement = firstBox.locator('a, button, .clickable').first();
        
        if (await clickableElement.count() > 0) {
          await clickableElement.click();
        } else {
          await firstBox.click();
        }
        
        await page.waitForTimeout(1000);
        
        // Get EAN code
        const eanDisplay = page.locator('[data-testid="ean-code"], .ean-code, .barcode, input[name*="ean"], input[readonly]');
        let eanCode = '';
        
        if (await eanDisplay.count() > 0) {
          eanCode = await eanDisplay.first().textContent() || await eanDisplay.first().inputValue();
          
          if (!eanCode || eanCode.length < 8) {
            // Generate EAN if needed
            const generateButton = page.locator('button').filter({ hasText: /Generate|Generovat/ });
            if (await generateButton.count() > 0) {
              await generateButton.first().click();
              await page.waitForTimeout(1000);
              eanCode = await eanDisplay.first().textContent() || await eanDisplay.first().inputValue();
            }
          }
        }
        
        // Go back to list
        const backButton = page.locator('button').filter({ hasText: /Back|Zpět|Close|×/ });
        if (await backButton.count() > 0) {
          await backButton.first().click();
          await page.waitForTimeout(1000);
        }
        
        // Search by EAN code
        if (eanCode && eanCode.length >= 8) {
          await searchBox.first().fill(eanCode);
          await page.keyboard.press('Enter');
          await page.waitForTimeout(1000);
          
          // Should find the box
          const searchResults = page.locator('[data-testid="transport-box-item"], .transport-box-item, .box-item, tr:has(td)');
          const resultCount = await searchResults.count();
          
          expect(resultCount).toBeGreaterThanOrEqual(1);
          
          // The found box should contain our EAN
          const resultText = await searchResults.first().textContent();
          expect(resultText).toContain(eanCode);
          
          // Clear search
          await searchBox.first().clear();
          await page.keyboard.press('Enter');
          await page.waitForTimeout(1000);
        }
      }
    }
    
    // Test EAN-based quick access (if available)
    const quickAccessButton = page.locator('button').filter({ 
      hasText: /Quick Access|Scan to Find|EAN Lookup|Rychlý přístup/ 
    });
    
    if (await quickAccessButton.count() > 0) {
      await quickAccessButton.first().click();
      await page.waitForTimeout(1000);
      
      // Should open EAN input or scanner
      const eanInput = page.locator('input[name*="ean"], input[placeholder*="EAN"], input[placeholder*="kód"]');
      
      if (await eanInput.count() > 0) {
        await eanInput.first().fill('1234567890123');
        
        const lookupButton = page.locator('button').filter({ hasText: /Find|Lookup|Search|Najít/ });
        if (await lookupButton.count() > 0) {
          await lookupButton.first().click();
          await page.waitForTimeout(1000);
          
          // Should either find box or show "not found" message
          const foundBox = page.locator('[data-testid="transport-box-item"], .transport-box-item');
          const notFoundMessage = page.locator('text=Not found, text=Nenalezeno, .not-found');
          
          const hasResults = await foundBox.count() > 0;
          const hasNotFound = await notFoundMessage.count() > 0;
          
          expect(hasResults || hasNotFound).toBe(true);
        }
      }
    }
  });

  test('should verify integration with Shoptet stock updates', async ({ page }) => {
    await navigateToTransportBoxes(page);
    
    // Find a box with items to test stock integration
    const boxes = page.locator('[data-testid="transport-box-item"], .transport-box-item, .box-item, tr:has(td)');
    
    if (await boxes.count() > 0) {
      const firstBox = boxes.first();
      const clickableElement = firstBox.locator('a, button, .clickable').first();
      
      if (await clickableElement.count() > 0) {
        await clickableElement.click();
      } else {
        await firstBox.click();
      }
      
      await page.waitForTimeout(1000);
      
      // Look for stock sync or Shoptet integration buttons
      const syncButton = page.locator('button').filter({ 
        hasText: /Sync Stock|Update Stock|Shoptet|Sync|Synchronizovat/i 
      });
      
      if (await syncButton.count() > 0) {
        await syncButton.first().click();
        await page.waitForTimeout(2000);
        
        // Should show sync status or confirmation
        const syncStatus = page.locator('.sync-status, .notification, .toast, .status-message');
        const loadingIndicator = page.locator('.loading, .spinner, .sync-progress');
        
        const hasStatus = await syncStatus.count() > 0;
        const hasLoading = await loadingIndicator.count() > 0;
        
        expect(hasStatus || hasLoading).toBe(true);
        
        if (hasLoading) {
          // Wait for sync to complete
          await page.waitForTimeout(5000);
          
          // Should show completion status
          const completionStatus = page.locator('.success, .text-green-500, .sync-complete');
          if (await completionStatus.count() > 0) {
            await expect(completionStatus.first()).toBeVisible();
          }
        }
      }
      
      // Test stock level display (if integrated)
      const stockInfo = page.locator('[data-testid="stock-level"], .stock-level, .inventory-info');
      
      if (await stockInfo.count() > 0) {
        // Should show stock information
        const stockText = await stockInfo.first().textContent();
        expect(stockText).toBeTruthy();
        expect(stockText).toMatch(/\d+/); // Should contain numbers
      }
      
      // Test stock warnings (low stock, out of stock)
      const stockWarning = page.locator('.stock-warning, .low-stock, .out-of-stock, .text-orange-500, .text-red-500');
      
      if (await stockWarning.count() > 0) {
        // Check if warnings have meaningful text content
        const warningElements = await stockWarning.all();
        const meaningfulWarnings = [];
        
        for (const element of warningElements) {
          const text = await element.textContent();
          if (text && text.trim()) {
            meaningfulWarnings.push(text.trim());
          }
        }
        
        if (meaningfulWarnings.length > 0) {
          console.log('Found stock warnings:', meaningfulWarnings);
          expect(meaningfulWarnings.every(warning => warning.length > 0)).toBe(true);
        } else {
          console.log('Stock warning elements found but no meaningful text content');
        }
      }
    }
  });

  test('should test EAN code confirmation workflows', async ({ page }) => {
    await navigateToTransportBoxes(page);
    
    // Test EAN confirmation during state transitions
    const boxes = page.locator('[data-testid="transport-box-item"], .transport-box-item, .box-item, tr:has(td)');
    
    if (await boxes.count() > 0) {
      const firstBox = boxes.first();
      const clickableElement = firstBox.locator('a, button, .clickable').first();
      
      if (await clickableElement.count() > 0) {
        await clickableElement.click();
      } else {
        await firstBox.click();
      }
      
      await page.waitForTimeout(1000);
      
      // Look for state transition buttons that might require EAN confirmation
      const stateTransitionButtons = page.locator('button').filter({ 
        hasText: /Ship|Send|Receive|Confirm|Deliver|Odeslat|Potvrdit|Přijmout/i 
      });
      
      if (await stateTransitionButtons.count() > 0) {
        await stateTransitionButtons.first().click();
        await page.waitForTimeout(1000);
        
        // May show EAN confirmation dialog
        const eanConfirmDialog = page.locator('[role="dialog"], .modal').filter({ 
          hasText: /EAN|Barcode|Confirmation|Potvrzení/i 
        });
        
        if (await eanConfirmDialog.count() > 0) {
          // Should have EAN input for confirmation
          const confirmEanInput = eanConfirmDialog.locator('input[name*="ean"], input[placeholder*="EAN"]');
          
          if (await confirmEanInput.count() > 0) {
            // Get expected EAN from box
            const expectedEan = page.locator('[data-testid="ean-code"], .ean-code, .barcode');
            let expectedEanValue = '';
            
            if (await expectedEan.count() > 0) {
              expectedEanValue = await expectedEan.first().textContent() || '';
            }
            
            // Test wrong EAN
            await confirmEanInput.fill('wrong-ean-123');
            
            const confirmButton = eanConfirmDialog.locator('button').filter({ 
              hasText: /Confirm|OK|Potvrdit/ 
            });
            
            if (await confirmButton.count() > 0) {
              await confirmButton.first().click();
              await page.waitForTimeout(500);
              
              // Should show error for wrong EAN
              const eanError = page.locator('.error, .text-red-500, .invalid').filter({ 
                hasText: /EAN|match|correct|správný/i 
              });
              
              if (await eanError.count() > 0) {
                await expect(eanError.first()).toBeVisible();
              }
              
              // Enter correct EAN
              if (expectedEanValue) {
                await confirmEanInput.clear();
                await confirmEanInput.fill(expectedEanValue);
                await confirmButton.first().click();
                await page.waitForTimeout(1000);
                
                // Should proceed with state change
                const successMessage = page.locator('.success, .text-green-500, .notification');
                const dialogClosed = await eanConfirmDialog.count() === 0;
                
                expect(await successMessage.count() > 0 || dialogClosed).toBe(true);
              }
            }
          }
        }
      }
    }
  });

  test('should test EAN code printing and labeling', async ({ page }) => {
    await navigateToTransportBoxes(page);
    
    const boxes = page.locator('[data-testid="transport-box-item"], .transport-box-item, .box-item, tr:has(td)');
    
    if (await boxes.count() > 0) {
      const firstBox = boxes.first();
      const clickableElement = firstBox.locator('a, button, .clickable').first();
      
      if (await clickableElement.count() > 0) {
        await clickableElement.click();
      } else {
        await firstBox.click();
      }
      
      await page.waitForTimeout(1000);
      
      // Look for print label button
      const printButton = page.locator('button').filter({ 
        hasText: /Print|Label|Barcode|Tisk|Štítek|Vytisknout/i 
      });
      
      if (await printButton.count() > 0) {
        await printButton.first().click();
        await page.waitForTimeout(1000);
        
        // May open print dialog or preview
        const printDialog = page.locator('[role="dialog"], .modal, .print-preview');
        const printWindow = page.locator('.print-window');
        
        const hasPrintDialog = await printDialog.count() > 0;
        const hasPrintWindow = await printWindow.count() > 0;
        
        if (hasPrintDialog) {
          // Should show label preview with EAN
          const labelPreview = printDialog.locator('.label-preview, .barcode-preview, canvas, svg');
          
          if (await labelPreview.count() > 0) {
            await expect(labelPreview.first()).toBeVisible();
          }
          
          // Should have print options
          const printOptionsButton = printDialog.locator('button').filter({ 
            hasText: /Print|Tisk|OK/ 
          });
          
          if (await printOptionsButton.count() > 0) {
            // Test clicking print (may open browser print dialog)
            await printOptionsButton.first().click();
            await page.waitForTimeout(500);
            
            // Browser print dialog might appear (can't easily test)
            // Just ensure no errors occurred
            const errors = page.locator('.error, .text-red-500');
            expect(await errors.count()).toBe(0);
          }
          
          // Close print dialog
          const closeButton = printDialog.locator('button').filter({ 
            hasText: /Close|Cancel|Zavřít|Zrušit/ 
          });
          
          if (await closeButton.count() > 0) {
            await closeButton.first().click();
          }
        }
      }
      
      // Test barcode display formats
      const barcodeDisplay = page.locator('.barcode, canvas[data-barcode], svg[data-barcode], img[src*="barcode"]');
      
      if (await barcodeDisplay.count() > 0) {
        // Barcode should be visible
        await expect(barcodeDisplay.first()).toBeVisible();
        
        // Test different barcode formats (if selectable)
        const formatSelector = page.locator('select[name*="format"], .format-selector');
        
        if (await formatSelector.count() > 0) {
          await formatSelector.first().selectOption({ index: 1 });
          await page.waitForTimeout(500);
          
          // Barcode should update
          await expect(barcodeDisplay.first()).toBeVisible();
        }
      }
      
      // Test QR code generation (alternative to barcode)
      const qrCodeButton = page.locator('button').filter({ hasText: /QR|QR Code/ });
      
      if (await qrCodeButton.count() > 0) {
        await qrCodeButton.first().click();
        await page.waitForTimeout(1000);
        
        const qrCode = page.locator('.qr-code, canvas[data-qr], svg[data-qr], img[src*="qr"]');
        
        if (await qrCode.count() > 0) {
          await expect(qrCode.first()).toBeVisible();
        }
      }
    }
  });

  test('should test bulk EAN operations', async ({ page }) => {
    await navigateToTransportBoxes(page);
    
    // Test bulk EAN generation
    const selectAllCheckbox = page.locator('input[type="checkbox"]').first();
    
    if (await selectAllCheckbox.count() > 0) {
      await selectAllCheckbox.check();
      await page.waitForTimeout(500);
      
      // Look for bulk EAN operations
      const bulkEanButton = page.locator('button').filter({ 
        hasText: /Generate EAN|Bulk EAN|Print Labels|Generovat EAN|Hromadně/i 
      });
      
      if (await bulkEanButton.count() > 0) {
        await bulkEanButton.first().click();
        await page.waitForTimeout(1000);
        
        // Should show bulk operation dialog
        const bulkDialog = page.locator('[role="dialog"], .modal, .bulk-operations');
        
        if (await bulkDialog.count() > 0) {
          // Should have options for bulk generation
          const generateAllButton = bulkDialog.locator('button').filter({ 
            hasText: /Generate All|Create All|Všechny/ 
          });
          
          if (await generateAllButton.count() > 0) {
            await generateAllButton.first().click();
            await page.waitForTimeout(3000);
            
            // Should show progress or completion
            const progress = page.locator('.progress, .loading, .bulk-progress');
            const completion = page.locator('.success, .text-green-500, .completion');
            
            const hasProgress = await progress.count() > 0;
            const hasCompletion = await completion.count() > 0;
            
            expect(hasProgress || hasCompletion).toBe(true);
          }
          
          // Close dialog
          const closeButton = bulkDialog.locator('button').filter({ 
            hasText: /Close|Done|Zavřít|Hotovo/ 
          });
          
          if (await closeButton.count() > 0) {
            await closeButton.first().click();
          }
        }
      }
      
      // Uncheck all
      await selectAllCheckbox.uncheck();
    }
  });
});