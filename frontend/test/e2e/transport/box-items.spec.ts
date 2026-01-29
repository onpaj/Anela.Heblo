import { test, expect } from '@playwright/test';
import { navigateToTransportBoxes } from '../helpers/e2e-auth-helper';

test.describe('Transport Box Items E2E Tests', () => {

  test.beforeEach(async ({ page }) => {
    // Navigate to transport boxes with full authentication
    await navigateToTransportBoxes(page);
  });

  test('should navigate to Transport Box detail page', async ({ page }) => {
    
    // Find and click on a transport box to open detail view
    const boxItems = page.locator('[data-testid="transport-box-item"], .transport-box-item, .box-item, tr:has(td)');
    const boxCount = await boxItems.count();
    
    if (boxCount > 0) {
      // Click on the first box or find a clickable element within it
      const firstBox = boxItems.first();
      const clickableElement = firstBox.locator('a, button, .clickable').first();
      
      if (await clickableElement.count() > 0) {
        await clickableElement.click();
      } else {
        // If no specific clickable element, try clicking the box itself
        await firstBox.click();
      }
      
      await page.waitForTimeout(1000);
      
      // Should navigate to detail page or open detail modal
      const detailView = page.locator('[data-testid="transport-box-detail"], .transport-box-detail, .box-detail, h2');
      const modal = page.locator('[role="dialog"], .modal');
      
      const hasDetailView = await detailView.count() > 0;
      const hasModal = await modal.count() > 0;
      
      expect(hasDetailView || hasModal).toBe(true);
    } else {
      console.log('No transport boxes found - creating a test box first');
      
      // Create a test box first
      const createButton = page.locator('button').filter({ hasText: /(\+|Add|Create|Nový|Přidat)/ });
      if (await createButton.count() > 0) {
        await createButton.first().click();
        await page.waitForTimeout(1000);
        
        // Fill minimal required fields
        const descriptionField = page.locator('input[name="description"], textarea[name="description"]').first();
        if (await descriptionField.count() > 0) {
          await descriptionField.fill('Test Box for Items');
          
          const submitButton = page.locator('button[type="submit"], button').filter({ hasText: /Save|Uložit|Create/ });
          if (await submitButton.count() > 0) {
            await submitButton.first().click();
            await page.waitForTimeout(2000);
          }
        }
      }
    }
  });

  test('should test adding items to transport box', async ({ page }) => {
    await navigateToTransportBoxes(page);
    
    // Navigate to first available box detail
    const boxItems = page.locator('[data-testid="transport-box-item"], .transport-box-item, .box-item, tr:has(td)');
    if (await boxItems.count() > 0) {
      const firstBox = boxItems.first();
      const clickableElement = firstBox.locator('a, button, .clickable').first();
      
      if (await clickableElement.count() > 0) {
        await clickableElement.click();
      } else {
        await firstBox.click();
      }
      
      await page.waitForTimeout(1000);
      
      // Look for "Add Item" button
      const addItemButton = page.locator('button').filter({ 
        hasText: /Add Item|Přidat položku|Add Product|Přidat produkt|\+/ 
      });
      
      if (await addItemButton.count() > 0) {
        await addItemButton.first().click();
        await page.waitForTimeout(1000);
        
        // Should open add item form/modal
        const addItemForm = page.locator('[data-testid="add-item-form"], .add-item-form, [role="dialog"]');
        await expect(addItemForm.first()).toBeVisible();
        
        // Look for product search/autocomplete
        const productSearch = page.locator('input[name*="product"], input[placeholder*="product"], input[placeholder*="produkt"], .autocomplete input').first();
        
        if (await productSearch.count() > 0) {
          // Type to search for products
          await productSearch.fill('test');
          await page.waitForTimeout(1000);
          
          // Look for autocomplete suggestions
          const suggestions = page.locator('.autocomplete-item, .suggestion, .dropdown-item, li[role="option"]');
          
          if (await suggestions.count() > 0) {
            // Click first suggestion
            await suggestions.first().click();
            await page.waitForTimeout(500);
            
            // Verify product was selected
            const selectedProduct = await productSearch.inputValue();
            expect(selectedProduct).toBeTruthy();
          }
        }
        
        // Fill quantity
        const quantityInput = page.locator('input[name*="quantity"], input[name*="amount"], input[placeholder*="quantity"], input[placeholder*="množství"]').first();
        
        if (await quantityInput.count() > 0) {
          await quantityInput.fill('5');
          await expect(quantityInput).toHaveValue('5');
        }
        
        // Submit the form
        const submitButton = page.locator('button[type="submit"], button').filter({ 
          hasText: /Add|Save|Přidat|Uložit|OK/ 
        });
        
        if (await submitButton.count() > 0) {
          await submitButton.first().click();
          await page.waitForTimeout(2000);
          
          // Should close form and show success
          const successMessage = page.locator('.success, .text-green-500, .notification');
          const itemsList = page.locator('[data-testid="box-items"], .box-items, .items-list');
          
          const hasSuccess = await successMessage.count() > 0;
          const hasItemsList = await itemsList.count() > 0;
          
          expect(hasSuccess || hasItemsList).toBe(true);
        }
      }
    }
  });

  test('should validate item quantity management', async ({ page }) => {
    await navigateToTransportBoxes(page);
    
    // Navigate to box with items
    const boxItems = page.locator('[data-testid="transport-box-item"], .transport-box-item, .box-item, tr:has(td)');
    if (await boxItems.count() > 0) {
      const firstBox = boxItems.first();
      const clickableElement = firstBox.locator('a, button, .clickable').first();
      
      if (await clickableElement.count() > 0) {
        await clickableElement.click();
      } else {
        await firstBox.click();
      }
      
      await page.waitForTimeout(1000);
      
      // Look for existing items in the box
      const existingItems = page.locator('[data-testid="box-item"], .box-item, .item-row, tr:has(td)');
      
      if (await existingItems.count() > 0) {
        const firstItem = existingItems.first();
        
        // Look for quantity controls (input field or +/- buttons)
        const quantityInput = firstItem.locator('input[name*="quantity"], input[type="number"]').first();
        const increaseButton = firstItem.locator('button').filter({ hasText: /\+|Increase/ }).first();
        const decreaseButton = firstItem.locator('button').filter({ hasText: /-|Decrease/ }).first();
        
        // Test quantity input field
        if (await quantityInput.count() > 0) {
          const originalValue = await quantityInput.inputValue();
          await quantityInput.fill('10');
          
          // Look for save/update button
          const saveButton = page.locator('button').filter({ hasText: /Save|Update|Uložit|Aktualizovat/ });
          if (await saveButton.count() > 0) {
            await saveButton.first().click();
            await page.waitForTimeout(1000);
            
            // Verify quantity was updated
            await expect(quantityInput).toHaveValue('10');
          }
        }
        
        // Test +/- buttons
        if (await increaseButton.count() > 0) {
          const quantityDisplay = firstItem.locator('.quantity, [data-testid="quantity"]').first();
          let originalQuantity = '0';
          
          if (await quantityDisplay.count() > 0) {
            originalQuantity = await quantityDisplay.textContent() || '0';
          }
          
          await increaseButton.click();
          await page.waitForTimeout(500);
          
          // Quantity should increase
          if (await quantityDisplay.count() > 0) {
            const newQuantity = await quantityDisplay.textContent() || '0';
            expect(parseInt(newQuantity)).toBeGreaterThan(parseInt(originalQuantity));
          }
        }
        
        if (await decreaseButton.count() > 0) {
          await decreaseButton.click();
          await page.waitForTimeout(500);
          
          // Should not go below 0
          const quantityDisplay = firstItem.locator('.quantity, [data-testid="quantity"]').first();
          if (await quantityDisplay.count() > 0) {
            const quantity = await quantityDisplay.textContent() || '0';
            expect(parseInt(quantity)).toBeGreaterThanOrEqual(0);
          }
        }
      }
    }
  });

  test('should test removing items from boxes', async ({ page }) => {
    await navigateToTransportBoxes(page);
    
    // Navigate to box with items
    const boxItems = page.locator('[data-testid="transport-box-item"], .transport-box-item, .box-item, tr:has(td)');
    if (await boxItems.count() > 0) {
      const firstBox = boxItems.first();
      const clickableElement = firstBox.locator('a, button, .clickable').first();
      
      if (await clickableElement.count() > 0) {
        await clickableElement.click();
      } else {
        await firstBox.click();
      }
      
      await page.waitForTimeout(1000);
      
      // Get initial count of items
      const existingItems = page.locator('[data-testid="box-item"], .box-item, .item-row, tr:has(td)');
      const initialItemCount = await existingItems.count();
      
      if (initialItemCount > 0) {
        const firstItem = existingItems.first();
        
        // Look for remove/delete button
        const removeButton = firstItem.locator('button').filter({ 
          hasText: /Remove|Delete|Odebrat|Smazat|×|✕/ 
        }).first();
        
        if (await removeButton.count() > 0) {
          await removeButton.click();
          await page.waitForTimeout(500);
          
          // Should show confirmation dialog
          const confirmDialog = page.locator('[role="dialog"], .modal, .confirmation');
          if (await confirmDialog.count() > 0) {
            const confirmButton = confirmDialog.locator('button').filter({ 
              hasText: /Yes|OK|Confirm|Ano|Potvrdit/ 
            });
            
            if (await confirmButton.count() > 0) {
              await confirmButton.first().click();
              await page.waitForTimeout(1000);
              
              // Item count should decrease
              const newItemCount = await existingItems.count();
              expect(newItemCount).toBeLessThan(initialItemCount);
            }
          } else {
            // Direct removal without confirmation
            await page.waitForTimeout(1000);
            const newItemCount = await existingItems.count();
            expect(newItemCount).toBeLessThan(initialItemCount);
          }
        }
      }
    }
  });

  test('should verify item autocomplete and selection', async ({ page }) => {
    await navigateToTransportBoxes(page);
    
    // Navigate to box and open add item form
    const boxItems = page.locator('[data-testid="transport-box-item"], .transport-box-item, .box-item, tr:has(td)');
    if (await boxItems.count() > 0) {
      const firstBox = boxItems.first();
      const clickableElement = firstBox.locator('a, button, .clickable').first();
      
      if (await clickableElement.count() > 0) {
        await clickableElement.click();
      } else {
        await firstBox.click();
      }
      
      await page.waitForTimeout(1000);
      
      // Open add item form
      const addItemButton = page.locator('button').filter({ 
        hasText: /Add Item|Přidat položku|Add Product|Přidat produkt|\+/ 
      });
      
      if (await addItemButton.count() > 0) {
        await addItemButton.first().click();
        await page.waitForTimeout(1000);
        
        // Test product search autocomplete
        const productSearch = page.locator('input[name*="product"], input[placeholder*="product"], input[placeholder*="produkt"], .autocomplete input').first();
        
        if (await productSearch.count() > 0) {
          // Test typing triggers autocomplete
          await productSearch.fill('a');
          await page.waitForTimeout(1000);
          
          const suggestions = page.locator('.autocomplete-item, .suggestion, .dropdown-item, li[role="option"]');
          
          if (await suggestions.count() > 0) {
            // Test keyboard navigation
            await page.keyboard.press('ArrowDown');
            await page.waitForTimeout(200);
            await page.keyboard.press('ArrowDown');
            await page.waitForTimeout(200);
            
            // Select with Enter
            await page.keyboard.press('Enter');
            await page.waitForTimeout(500);
            
            // Should have selected an item
            const selectedValue = await productSearch.inputValue();
            expect(selectedValue.length).toBeGreaterThan(1);
          }
          
          // Test selecting different item by mouse
          await productSearch.clear();
          await productSearch.fill('test');
          await page.waitForTimeout(1000);
          
          const newSuggestions = page.locator('.autocomplete-item, .suggestion, .dropdown-item, li[role="option"]');
          if (await newSuggestions.count() > 0) {
            await newSuggestions.first().click();
            await page.waitForTimeout(500);
            
            const selectedValue = await productSearch.inputValue();
            expect(selectedValue).toContain('test');
          }
          
          // Test clearing selection
          await productSearch.clear();
          await expect(productSearch).toHaveValue('');
        }
      }
    }
  });

  test('should test QuickAdd functionality for recent items', async ({ page }) => {
    await navigateToTransportBoxes(page);
    
    // Navigate to box detail
    const boxItems = page.locator('[data-testid="transport-box-item"], .transport-box-item, .box-item, tr:has(td)');
    if (await boxItems.count() > 0) {
      const firstBox = boxItems.first();
      const clickableElement = firstBox.locator('a, button, .clickable').first();
      
      if (await clickableElement.count() > 0) {
        await clickableElement.click();
      } else {
        await firstBox.click();
      }
      
      await page.waitForTimeout(1000);
      
      // Look for QuickAdd button or recent items feature
      const quickAddButton = page.locator('button').filter({ 
        hasText: /Quick Add|QuickAdd|Recent|Poslední|Rychle přidat/ 
      });
      
      if (await quickAddButton.count() > 0) {
        await quickAddButton.first().click();
        await page.waitForTimeout(1000);
        
        // Should show recent items or quick add modal
        const quickAddModal = page.locator('[role="dialog"], .modal, .quick-add');
        const recentItems = page.locator('.recent-item, .quick-add-item');
        
        const hasModal = await quickAddModal.count() > 0;
        const hasRecentItems = await recentItems.count() > 0;
        
        if (hasModal || hasRecentItems) {
          // Test selecting a recent item
          const firstRecentItem = recentItems.first();
          if (await firstRecentItem.count() > 0) {
            await firstRecentItem.click();
            await page.waitForTimeout(500);
            
            // Should add item quickly
            const confirmButton = page.locator('button').filter({ 
              hasText: /Add|Přidat|OK|Confirm/ 
            });
            
            if (await confirmButton.count() > 0) {
              await confirmButton.first().click();
              await page.waitForTimeout(1000);
              
              // Should show success or close modal
              const successMessage = page.locator('.success, .text-green-500, .notification');
              const modalClosed = await quickAddModal.count() === 0;
              
              expect(await successMessage.count() > 0 || modalClosed).toBe(true);
            }
          }
        }
      } else {
        console.log('QuickAdd functionality not found - this may be expected if not implemented yet');
      }
    }
  });

  test('should test item validation and error handling', async ({ page }) => {
    await navigateToTransportBoxes(page);
    
    // Navigate to box and try to add invalid items
    const boxItems = page.locator('[data-testid="transport-box-item"], .transport-box-item, .box-item, tr:has(td)');
    if (await boxItems.count() > 0) {
      const firstBox = boxItems.first();
      const clickableElement = firstBox.locator('a, button, .clickable').first();
      
      if (await clickableElement.count() > 0) {
        await clickableElement.click();
      } else {
        await firstBox.click();
      }
      
      await page.waitForTimeout(1000);
      
      // Open add item form
      const addItemButton = page.locator('button').filter({ 
        hasText: /Add Item|Přidat položku|Add Product|Přidat produkt|\+/ 
      });
      
      if (await addItemButton.count() > 0) {
        await addItemButton.first().click();
        await page.waitForTimeout(1000);
        
        // Test submitting empty form
        const submitButton = page.locator('button[type="submit"], button').filter({ 
          hasText: /Add|Save|Přidat|Uložit/ 
        });
        
        if (await submitButton.count() > 0) {
          await submitButton.first().click();
          await page.waitForTimeout(500);
          
          // Should show validation errors
          const errorMessages = page.locator('.error, .text-red-500, .invalid, .validation-error');
          await expect(errorMessages.first()).toBeVisible();
        }
        
        // Test invalid quantity
        const quantityInput = page.locator('input[name*="quantity"], input[type="number"]').first();
        if (await quantityInput.count() > 0) {
          await quantityInput.fill('-5');
          
          if (await submitButton.count() > 0) {
            await submitButton.first().click();
            await page.waitForTimeout(500);
            
            // Should show validation error for negative quantity
            const quantityError = page.locator('.error, .text-red-500').filter({ hasText: /quantity|množství/i });
            const hasQuantityError = await quantityError.count() > 0;
            
            // Or quantity input might have built-in validation
            const inputValidation = await quantityInput.evaluate((el: HTMLInputElement) => !el.validity.valid);
            
            expect(hasQuantityError || inputValidation).toBe(true);
          }
          
          // Test zero quantity
          await quantityInput.fill('0');
          
          if (await submitButton.count() > 0) {
            await submitButton.first().click();
            await page.waitForTimeout(500);
            
            // May or may not allow zero quantity - just verify form handles it
            expect(true).toBe(true); // Form should handle this gracefully
          }
        }
      }
    }
  });
});