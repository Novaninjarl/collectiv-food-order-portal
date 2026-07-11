const API_BASE = 'http://localhost:5000/api';

export type Product = {
  sku: string;
  name: string;
};

export type ProductRecommendation = {
  sku: string;
  name: string;
  score: number;
  suggestedUnitPrice?: number | null;
  reason: string;
};

export type OrderItem = {
  productSku: string;
  productName: string;
  quantity: number;
  itemTotal: number;
};

export type StockReservation = {
  productSku: string;
  stockBatchId: string;
  quantity: number;
};

export type Order = {
  id: number;
  customerId: number;
  customerName: string;
  deliveryAddress: {
    addressLine1: string;
    postcode: string;
    city: string;
  };
  deliveryDate: string;
  total: number;
  items: OrderItem[];
  stockReservations: StockReservation[];
};

export type RejectedOrder = {
  id: number;
  sourceOrderId?: number;
  customerName: string;
  reasons: string[];
  createdAt: string;
};

export type Customer = {
  id: number;
  name: string;
  addressLine1: string;
  postcode: string;
  city: string;
  orderCount: number;
  lastOrderDate?: string;
};

export type EventLog = {
  id: number;
  type: string;
  orderId?: number;
  customerId?: number;
  message: string;
  createdAt: string;
};

export type CreateOrderRequest = {
  customerName: string;
  deliveryAddress: {
    addressLine1: string;
    postcode: string;
    city: string;
  };
  deliveryDate: string;
  items: {
    productSku: string;
    quantity: number;
    itemTotal: number;
  }[];
};

export type PlannedStockReservation = {
  productSku: string;
  productName: string;
  stockBatchId: string;
  quantity: number;
  expiryDate: string;
};

export type OrderPreviewResponse = {
  canSubmit: boolean;
  reasons: string[];
  warnings: string[];
  deliveryDate?: string | null;
  total: number;
  items: {
    productSku: string;
    productName: string;
    quantity: number;
    itemTotal: number;
  }[];
  plannedReservations: PlannedStockReservation[];
};

type RequestOptions = RequestInit & {
  customerId?: number | null;
};

async function request<T>(path: string, options?: RequestOptions): Promise<T> {
  const headers: Record<string, string> = { 'Content-Type': 'application/json' };

  if (options?.customerId) {
    headers['X-Customer-Id'] = String(options.customerId);
  }

  const response = await fetch(`${API_BASE}${path}`, {
    ...options,
    headers: {
      ...headers,
      ...(options?.headers ?? {})
    }
  });

  const body = await response.json().catch(() => null);

  if (!response.ok) {
    const reasons = body?.reasons?.join('\n') ?? 'Request failed';
    throw new Error(reasons);
  }

  return body as T;
}

export const api = {
  products: () => request<Product[]>('/products'),
  customers: () => request<Customer[]>('/customers'),

  // Customer-scoped portal endpoints. These use a mock customer session header.
  me: (customerId: number) => request<Customer>('/me', { customerId }),
  myOrders: (customerId: number) => request<Order[]>('/me/orders', { customerId }),
  myRejectedOrders: (customerId: number) => request<RejectedOrder[]>('/me/rejected-orders', { customerId }),
  myEvents: (customerId: number) => request<EventLog[]>('/me/events', { customerId }),
  myRecommendations: (customerId: number, query: string, limit = 6) => {
    const params = new URLSearchParams({ query, limit: String(limit) });
    return request<ProductRecommendation[]>(`/me/recommendations?${params.toString()}`, { customerId });
  },
  createMyOrder: (customerId: number, payload: CreateOrderRequest) =>
    request<Order>('/me/orders', {
      method: 'POST',
      body: JSON.stringify(payload),
      customerId
    }),
  previewMyOrder: (customerId: number, payload: CreateOrderRequest) =>
    request<OrderPreviewResponse>('/me/orders/preview', {
      method: 'POST',
      body: JSON.stringify(payload),
      customerId
  }),
  previewOrder: (payload: CreateOrderRequest) =>
    request<OrderPreviewResponse>('/orders/preview', {
    method: 'POST',
    body: JSON.stringify(payload)
  }),
  myReorder: (customerId: number, orderId: number, deliveryDate: string) =>
    request<Order>(`/me/orders/${orderId}/reorder`, {
      method: 'POST',
      body: JSON.stringify({ deliveryDate }),
      customerId
    }),

  // Admin/demo endpoints used to prove the full imported dataset and rejection reasons.
  orders: () => request<Order[]>('/orders'),
  rejectedOrders: () => request<RejectedOrder[]>('/rejected-orders'),
  events: () => request<EventLog[]>('/events'),
  recommendations: (query: string, customerId?: number | null, limit = 6) => {
    const params = new URLSearchParams({ query, limit: String(limit) });
    if (customerId) params.set('customerId', String(customerId));
    return request<ProductRecommendation[]>(`/recommendations?${params.toString()}`);
  },
  createOrder: (payload: CreateOrderRequest) =>
    request<Order>('/orders', {
      method: 'POST',
      body: JSON.stringify(payload)
    }),
  reorder: (orderId: number, deliveryDate: string) =>
    request<Order>(`/orders/${orderId}/reorder`, {
      method: 'POST',
      body: JSON.stringify({ deliveryDate })
    })
};
