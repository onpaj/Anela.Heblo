import { test, expect } from '@playwright/test';

test.describe('Purchase Orders API', () => {
  const baseURL = 'http://localhost:5001';

  test('should create purchase order with lines via API', async ({ request }) => {
    const createRequest = {
      supplierName: 'API Test Supplier',
      orderDate: '2024-08-02',
      expectedDeliveryDate: '2024-08-16',
      notes: 'API test order with lines',
      lines: [
        {
          materialId: 'MAT001',
          quantity: 10,
          unitPrice: 25.50,
          notes: 'First API test line'
        },
        {
          materialId: 'MAT002',
          quantity: 5,
          unitPrice: 15.00,
          notes: 'Second API test line'
        }
      ]
    };

    // Create purchase order
    const createResponse = await request.post(`${baseURL}/api/purchase-orders`, {
      data: createRequest,
      headers: {
        'Content-Type': 'application/json'
      }
    });

    expect(createResponse.status()).toBe(201);
    
    const createdOrder = await createResponse.json();
    expect(createdOrder).toHaveProperty('id');
    expect(createdOrder).toHaveProperty('orderNumber');
    expect(createdOrder.supplierName).toBe('API Test Supplier');
    expect(createdOrder.totalAmount).toBe(330.00); // 10*25.50 + 5*15.00
    
    // **CRITICAL: Verify lines are returned in creation response**
    expect(createdOrder.lines).toHaveLength(2);
    expect(createdOrder.lines[0].quantity).toBe(10);
    expect(createdOrder.lines[0].unitPrice).toBe(25.50);
    expect(createdOrder.lines[0].lineTotal).toBe(255.00);
    expect(createdOrder.lines[0].notes).toBe('First API test line');
    
    expect(createdOrder.lines[1].quantity).toBe(5);
    expect(createdOrder.lines[1].unitPrice).toBe(15.00);
    expect(createdOrder.lines[1].lineTotal).toBe(75.00);
    expect(createdOrder.lines[1].notes).toBe('Second API test line');

    // **CRITICAL: Fetch the order again to verify persistence**
    const getResponse = await request.get(`${baseURL}/api/purchase-orders/${createdOrder.id}`);
    expect(getResponse.status()).toBe(200);
    
    const fetchedOrder = await getResponse.json();
    expect(fetchedOrder.id).toBe(createdOrder.id);
    expect(fetchedOrder.supplierName).toBe('API Test Supplier');
    expect(fetchedOrder.totalAmount).toBe(330.00);
    
    // **CRITICAL: Verify lines are persisted in database**
    expect(fetchedOrder.lines).toHaveLength(2);
    expect(fetchedOrder.lines[0].quantity).toBe(10);
    expect(fetchedOrder.lines[0].unitPrice).toBe(25.50);
    expect(fetchedOrder.lines[0].lineTotal).toBe(255.00);
    expect(fetchedOrder.lines[0].notes).toBe('First API test line');
    
    expect(fetchedOrder.lines[1].quantity).toBe(5);
    expect(fetchedOrder.lines[1].unitPrice).toBe(15.00);
    expect(fetchedOrder.lines[1].lineTotal).toBe(75.00);
    expect(fetchedOrder.lines[1].notes).toBe('Second API test line');
  });

  test('should create purchase order without lines', async ({ request }) => {
    const createRequest = {
      supplierName: 'API Test Supplier No Lines',
      orderDate: '2024-08-02',
      expectedDeliveryDate: '2024-08-16',
      notes: 'API test order without lines',
      lines: [] // Empty lines array
    };

    const createResponse = await request.post(`${baseURL}/api/purchase-orders`, {
      data: createRequest,
      headers: {
        'Content-Type': 'application/json'
      }
    });

    expect(createResponse.status()).toBe(201);
    
    const createdOrder = await createResponse.json();
    expect(createdOrder.supplierName).toBe('API Test Supplier No Lines');
    expect(createdOrder.totalAmount).toBe(0);
    expect(createdOrder.lines).toHaveLength(0);

    // Verify persistence
    const getResponse = await request.get(`${baseURL}/api/purchase-orders/${createdOrder.id}`);
    expect(getResponse.status()).toBe(200);
    
    const fetchedOrder = await getResponse.json();
    expect(fetchedOrder.lines).toHaveLength(0);
    expect(fetchedOrder.totalAmount).toBe(0);
  });

  test('should handle missing lines property gracefully', async ({ request }) => {
    const createRequest = {
      supplierName: 'API Test Supplier Missing Lines',
      orderDate: '2024-08-02',
      expectedDeliveryDate: '2024-08-16',
      notes: 'API test order with missing lines property'
      // No lines property at all
    };

    const createResponse = await request.post(`${baseURL}/api/purchase-orders`, {
      data: createRequest,
      headers: {
        'Content-Type': 'application/json'
      }
    });

    expect(createResponse.status()).toBe(201);
    
    const createdOrder = await createResponse.json();
    expect(createdOrder.supplierName).toBe('API Test Supplier Missing Lines');
    expect(createdOrder.totalAmount).toBe(0);
    expect(createdOrder.lines).toHaveLength(0);
  });

  test('should update purchase order with lines', async ({ request }) => {
    // First create an order
    const createRequest = {
      supplierName: 'API Update Test Supplier',
      orderDate: '2024-08-02',
      notes: 'Order to be updated',
      lines: [
        {
          materialId: 'MAT001',
          quantity: 3,
          unitPrice: 10.00,
          notes: 'Original line'
        }
      ]
    };

    const createResponse = await request.post(`${baseURL}/api/purchase-orders`, {
      data: createRequest,
      headers: {
        'Content-Type': 'application/json'
      }
    });

    expect(createResponse.status()).toBe(201);
    const createdOrder = await createResponse.json();

    // Now update it
    const updateRequest = {
      id: createdOrder.id,
      supplierName: 'API Updated Supplier',
      expectedDeliveryDate: '2024-08-20',
      notes: 'Updated order',
      lines: [
        {
          materialId: 'MAT001',
          quantity: 5, // Changed quantity
          unitPrice: 10.00,
          notes: 'Updated line'
        },
        {
          materialId: 'MAT002', // New line
          quantity: 2,
          unitPrice: 15.00,
          notes: 'New line added'
        }
      ]
    };

    const updateResponse = await request.put(`${baseURL}/api/purchase-orders/${createdOrder.id}`, {
      data: updateRequest,
      headers: {
        'Content-Type': 'application/json'
      }
    });

    expect(updateResponse.status()).toBe(200);
    
    const updatedOrder = await updateResponse.json();
    expect(updatedOrder.supplierName).toBe('API Updated Supplier');
    expect(updatedOrder.totalAmount).toBe(80.00); // 5*10 + 2*15
    expect(updatedOrder.lines).toHaveLength(2);

    // Verify persistence
    const getResponse = await request.get(`${baseURL}/api/purchase-orders/${createdOrder.id}`);
    const fetchedOrder = await getResponse.json();
    
    expect(fetchedOrder.lines).toHaveLength(2);
    expect(fetchedOrder.lines[0].quantity).toBe(5);
    expect(fetchedOrder.lines[0].notes).toBe('Updated line');
    expect(fetchedOrder.lines[1].materialId).toBe('MAT002');
    expect(fetchedOrder.lines[1].notes).toBe('New line added');
  });

  test('should validate required fields', async ({ request }) => {
    const invalidRequest = {
      // Missing supplierName
      orderDate: '2024-08-02',
      lines: []
    };

    const response = await request.post(`${baseURL}/api/purchase-orders`, {
      data: invalidRequest,
      headers: {
        'Content-Type': 'application/json'
      }
    });

    expect(response.status()).toBe(400);
  });

  test('should handle invalid date formats', async ({ request }) => {
    const invalidRequest = {
      supplierName: 'Test Supplier',
      orderDate: 'invalid-date',
      lines: []
    };

    const response = await request.post(`${baseURL}/api/purchase-orders`, {
      data: invalidRequest,
      headers: {
        'Content-Type': 'application/json'
      }
    });

    expect(response.status()).toBe(400);
  });

  test('should list purchase orders with correct totals', async ({ request }) => {
    // Create a few orders first
    await request.post(`${baseURL}/api/purchase-orders`, {
      data: {
        supplierName: 'List Test Supplier 1',
        orderDate: '2024-08-02',
        lines: [
          { materialId: 'MAT001', quantity: 2, unitPrice: 50.00, notes: 'Line 1' }
        ]
      }
    });

    await request.post(`${baseURL}/api/purchase-orders`, {
      data: {
        supplierName: 'List Test Supplier 2',
        orderDate: '2024-08-02',
        lines: [
          { materialId: 'MAT002', quantity: 1, unitPrice: 75.00, notes: 'Line 2' }
        ]
      }
    });

    // Get the list
    const listResponse = await request.get(`${baseURL}/api/purchase-orders`);
    expect(listResponse.status()).toBe(200);
    
    const ordersList = await listResponse.json();
    expect(ordersList.orders).toBeDefined();
    expect(ordersList.totalCount).toBeGreaterThanOrEqual(2);
    
    // Find our test orders and verify totals
    const order1 = ordersList.orders.find((o: any) => o.supplierName === 'List Test Supplier 1');
    const order2 = ordersList.orders.find((o: any) => o.supplierName === 'List Test Supplier 2');
    
    expect(order1).toBeDefined();
    expect(order1.totalAmount).toBe(100.00);
    
    expect(order2).toBeDefined();
    expect(order2.totalAmount).toBe(75.00);
  });
});