# Collectiv Food Customer Order Portal

A customer-facing order management tool for professional kitchens.

Customers can log in through a mock customer selector, view their own order history, build a basket from the valid product catalogue, check availability before submitting, create orders, and repeat previous baskets.

The backend imports the provided `orders.json` dataset, stores only valid orders, and records rejected orders with clear validation reasons.

---

## Tech Stack

### Backend

- C# / .NET 8 Minimal API
- Entity Framework Core
- SQLite
- Seeded JSON data

### Frontend

- Vue 3
- Vite
- TypeScript
- CSS glassmorphism UI

---

## Features

### Required challenge features

- Imports orders from `orders.json`
- Validates orders before persistence
- Stores only valid orders
- Records rejected orders with clear reasons
- Exposes API endpoints to create and retrieve orders
- Provides a frontend form to create orders
- Displays accepted orders in the frontend

### Additional features

- Customer-scoped portal
- Mock customer login
- Private customer order history
- Admin/demo view for all imported orders and rejected orders
- Smart Order Assistant using simple semantic matching
- Product recommendations based on:
  - catalogue product names
  - scenario keywords
  - product popularity
  - previous customer orders
- Reorder previous basket
- Stock batch validation
- Expiry-date-aware stock allocation
- Earliest-expiring-first stock reservation
- Availability preview before order submission
- Event log for important actions such as order creation, rejection and stock reservation

---

## How to Run

### Prerequisites

Install:

- .NET 8 SDK
- Node.js 18+
- npm

Check installations:

```bash
dotnet --version
node --version
npm --version
```

---

## Backend Setup

From the project root:

```bash
cd backend
dotnet restore
dotnet run --urls http://localhost:5000
```

The backend runs at:

```txt
http://localhost:5000
```

The SQLite database is created automatically on startup.

The app seeds:

- products from `SeedData/products.json`
- orders from `SeedData/orders.json`
- stock batches from `SeedData/stockBatches.json`

To reset the database during testing:

```bash
cd backend
rm -f collectiv-orders-v3.db
dotnet run --urls http://localhost:5000
```

---

## Frontend Setup

In a second terminal:

```bash
cd frontend
npm install
npm run dev
```

The frontend runs at:

```txt
http://localhost:5173
```

---

## How to Use the App

### Public view

The logged-out view shows:

- product catalogue
- mock customer login selector
- explanation of the customer portal

Customers cannot see everyone’s order history while logged out.

### Customer portal

After selecting a customer, the user can:

- view their own accepted orders
- view their own rejected submissions
- create a new order
- use the Smart Order Assistant
- check availability before submitting
- reorder a previous basket

### Admin/demo view

The admin/demo view shows:

- all accepted imported orders
- all rejected imported orders
- all customers
- event log

This exists to make the assessment easy to review. In a real production app this would require proper admin authentication and authorization.

---

## API Endpoints

### Public/demo endpoints

```txt
GET  /api/products
GET  /api/customers
GET  /api/recommendations?query=breakfast&customerId=1&limit=6
POST /api/orders/preview
POST /api/orders
GET  /api/orders
GET  /api/rejected-orders
GET  /api/events
POST /api/orders/{orderId}/reorder
```

### Customer-scoped endpoints

These endpoints use the `X-Customer-Id` header.

```txt
GET  /api/me/orders
GET  /api/me/rejected-orders
GET  /api/me/recommendations?query=breakfast&limit=6
POST /api/me/orders/preview
POST /api/me/orders
POST /api/me/orders/{orderId}/reorder
```

Example customer-scoped request:

```bash
curl -H "X-Customer-Id: 1" http://localhost:5000/api/me/orders
```

---

## Validation Rules

An order is accepted only if:

- customer name is present
- delivery address is present
- postcode is present
- city is present
- delivery date is a real date in `DD/MM/YYYY` format
- the order has at least one item
- every item has a product SKU
- every SKU exists in `products.json`
- quantity is a positive whole number
- item total is positive
- item total has no more than 2 decimal places
- order total matches the sum of item totals
- imported order IDs are unique
- enough non-expired stock is available for the delivery date

Invalid orders are rejected and stored with clear reasons.

---

## Stock and Expiry Logic

The project includes a small seeded stock batch dataset to demonstrate how order validation could account for availability and expiry dates.

For each order item:

1. Find stock batches for the product SKU.
2. Ignore batches that expire before the delivery date.
3. Sort remaining batches by earliest expiry date.
4. Allocate from earliest-expiring stock first.
5. Reject the order if there is not enough valid stock.

This follows a FEFO-style approach: first-expiring, first-out.

The availability preview uses the same rules but does not reserve stock. The actual submit endpoint validates and reserves stock again server-side.

---

## Smart Order Assistant

The Smart Order Assistant helps customers find catalogue products using simple, explainable semantics.

Example queries:

```txt
breakfast for a cafe
bakery restock
drinks for lunch
seafood menu
```

The assistant scores products using:

- product name matches
- scenario keyword matches
- previous customer orders
- product popularity across accepted orders

It only recommends products that exist in the product catalogue. It does not invent new products.

I chose a deterministic scoring approach instead of an external AI API so the feature is:

- easy to run locally
- explainable
- testable
- safe for a short assessment

With more time, this could be extended with embeddings or an LLM-powered natural language ordering assistant.

---

## Design Decisions and Tradeoffs

### 1. C# / .NET backend

I used .NET 8 Minimal API because the target role includes C#/.NET backend work. Minimal API keeps the implementation lightweight while still showing API design, service separation and database-backed persistence.

### 2. SQLite instead of PostgreSQL

The role mentions PostgreSQL, but I used SQLite to keep the project easy to run locally without requiring a database server.

Entity Framework Core keeps the data model relational, so the project could be moved to PostgreSQL later by changing the provider and connection string.

### 3. Mock login instead of real authentication

The app uses a customer selector and `X-Customer-Id` header to simulate customer login.

This keeps the project focused on the core order-management problem. In production, this should be replaced with proper authentication, authorization, passwordless login or SSO.

### 4. Customer-scoped order history

The challenge asks for a page that retrieves and displays orders. I separated the app into:

- a customer portal showing only the logged-in customer’s orders
- an admin/demo view showing all orders for review

This is more realistic for a client-facing portal because customer orders contain names, addresses, postcodes and delivery details.

### 5. Product catalogue as source of truth

Only products in `products.json` can be ordered.

SKUs are normalized by trimming whitespace and converting to uppercase before validation. This allows casing mistakes such as `ab12CD34EF56` to be accepted if they match a valid catalogue SKU after normalization.

Unknown SKUs are rejected.

### 6. Added stock batch seed data

The original challenge does not provide stock data, but the bonus asks how validation could account for stock availability and stock expiry.

I added a small `stockBatches.json` seed file to demonstrate this logic in a working way.

### 7. Preview does not reserve stock

The availability preview simulates stock allocation but does not reduce stock quantities.

The submit endpoint repeats validation and allocation server-side because preview data can become stale if another order reserves stock before the customer submits.

### 8. Event log instead of real message queue

The app records events in an `EventLog` table.

In production, these events could be published to SNS/SQS or another message queue so downstream services could handle fulfilment, notifications, analytics or stock updates asynchronously.

---

## Known Limitations

- Authentication is mocked.
- There is no role-based access control.
- The stock data is seeded demo data.
- There is no payment, invoicing or delivery-slot system.
- Product prices are not provided, so item totals are entered by the customer and validated structurally.
- There is no real supplier integration.
- Availability preview can become stale before submission.
- There are no automated tests yet.
- The frontend is optimized for demonstration rather than full accessibility or production design-system compliance.

---

## What I Would Improve With More Time

### Production authentication and authorization

Replace mock login with real authentication and customer-scoped authorization.

Customers should only be able to access their own orders. Admin views should require admin permissions.

### PostgreSQL persistence

Move from SQLite to PostgreSQL and add migrations for a more production-like setup.

### Automated tests

Add backend tests for:

- invalid date rejection
- unknown SKU rejection
- duplicate order ID rejection
- empty order rejection
- mixed-case SKU normalization
- money precision validation
- insufficient stock rejection
- earliest-expiring-first allocation
- preview not mutating stock

### Better pricing model

The provided products do not include unit prices.

In a production system, item totals should be calculated server-side from a trusted pricing table or pricing service rather than entered by the customer.

### Delivery slot validation

Add delivery constraints such as:

- no delivery on unavailable days
- customer-specific delivery windows
- cut-off times for next-day delivery
- postcode-based delivery rules

### Stock reservation lifecycle

Add reservation expiry and cancellation logic.

For example:

- reserve stock when an order is submitted
- release stock when an order is cancelled
- expire unpaid or abandoned reservations
- handle partial fulfilment or backorders

### Standing orders and subscriptions

Add recurring orders for customers who regularly buy the same products.

For example, a cafe could automatically place a weekly order for milk, eggs and bread.

### Event-driven architecture

Replace the local event log with an outbox pattern and a queue.

Possible events:

```txt
ORDER_CREATED
ORDER_REJECTED
STOCK_RESERVED
STOCK_RELEASED
REORDER_CREATED
CUSTOMER_NOTIFICATION_REQUESTED
```

These could be consumed by fulfilment, notification, analytics or supplier-integration services.

### AI/semantic search upgrade

Replace the simple keyword/scenario recommender with embeddings or an LLM-powered assistant.

The assistant could support natural language requests such as:

```txt
I need breakfast supplies for 40 people next Monday.
```

It could then suggest products, quantities and delivery options while still validating against the catalogue and stock data.

---

## AI Usage

I used AI assistance to speed up scaffolding, styling and implementation planning.

I treated generated code as a draft and reviewed the logic manually, especially around:

- validation rules
- customer scoping
- stock allocation
- preview not mutating stock
- order submission revalidation
- security/privacy tradeoffs

The main product and technical decisions were made around the challenge requirements: build a client-facing order tool, validate dirty data, store valid orders, reject invalid orders clearly, and explain assumptions and tradeoffs.